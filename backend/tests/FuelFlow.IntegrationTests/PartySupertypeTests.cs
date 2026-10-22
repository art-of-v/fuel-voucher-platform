using FluentAssertions;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// D-02, additive step 1 - the <c>parties</c> supertype and <c>natural_persons</c>, inserted and
/// back-filled, with nothing switched over to reading them yet.
/// <para>
/// The load-bearing claim is that after this migration every user has a natural-person party, both
/// for rows that already existed and for rows inserted afterwards by any path. That is what makes a
/// later migration able to promote <c>users.party_id</c> to NOT NULL without a second sweep - so it
/// is asserted here, where the sweep can still be cheap.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public sealed class PartySupertypeTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public PartySupertypeTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    private const string InsertUser = """
        INSERT INTO users (id, phone_number, first_name, last_name, birthdate, party_id, created_at_utc, updated_at_utc)
        VALUES ({0}, {1}, {2}, {3}, {4}, {5}, NOW(), NOW())
        """;

    /// <summary>A syntactically plausible unique phone number, without the collisions tests create.</summary>
    private static string NewPhone() => $"+38050{Guid.NewGuid().ToString("N")[..7]}";

    [Fact]
    public async Task Every_User_Already_Present_Has_A_Natural_Person_Party()
    {
        await using var context = CreateContext();

        // The back-fill ran during the migration, so this is a statement about the migration, not
        // about anything a handler did.
        var orphans = await context.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value"
            FROM users u
            WHERE u.party_id IS NULL
               OR NOT EXISTS (SELECT 1 FROM natural_persons np WHERE np.party_id = u.party_id)
               OR NOT EXISTS (SELECT 1 FROM parties p WHERE p.id = u.party_id AND p.party_type = 'Natural')
            """).ToListAsync();

        orphans.Should().ContainSingle().Which.Should().Be(0);
    }

    [Fact]
    public async Task A_User_Inserted_Now_Gets_A_Party_Without_Anyone_Asking()
    {
        await using var context = CreateContext();
        var userId = Guid.NewGuid();
        var phone = NewPhone();

        try
        {
            await context.Database.ExecuteSqlRawAsync(
                InsertUser, userId, phone, "Тест", "Гончар", new DateOnly(1990, 5, 1), null!);

            var partyId = await context.Database.SqlQuery<Guid?>($"""
                SELECT u.party_id AS "Value" FROM users u WHERE u.id = {userId}
                """).FirstAsync();

            partyId.Should().NotBeNull("the trigger creates the party, not the registration handler");

            // Scalars rather than a projected tuple: SqlQuery composes each of these into its own
            // subquery, and a multi-column tuple does not survive that composition.
            var name = await context.Database.SqlQuery<string>($"""
                SELECT p.display_name AS "Value"
                FROM users u
                JOIN parties p ON p.id = u.party_id
                WHERE u.id = {userId}
                """).FirstAsync();

            var partyType = await context.Database.SqlQuery<string>($"""
                SELECT p.party_type AS "Value"
                FROM users u
                JOIN parties p ON p.id = u.party_id
                WHERE u.id = {userId}
                """).FirstAsync();

            var birthdate = await context.Database.SqlQuery<DateOnly?>($"""
                SELECT np.birthdate AS "Value"
                FROM users u
                JOIN natural_persons np ON np.party_id = u.party_id
                WHERE u.id = {userId}
                """).FirstAsync();

            partyType.Should().Be("Natural");
            name.Should().Be("Тест Гончар");
            birthdate.Should().Be(new DateOnly(1990, 5, 1));
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                DELETE FROM natural_persons np
                USING users u WHERE u.party_id = np.party_id AND u.id = {0}
                """, userId);
            // The user goes before the party: users.party_id is a foreign key now, and a party a
            // user still points at cannot be deleted. The party id is read off the user BEFORE the
            // user is deleted, because afterwards there is nothing left to read it from.
            var party = await context.Database.SqlQuery<Guid?>($"""
                SELECT party_id AS "Value" FROM users WHERE id = {userId}
                """).FirstAsync();
            await context.Database.ExecuteSqlRawAsync("DELETE FROM users WHERE id = {0}", userId);
            await context.Database.ExecuteSqlRawAsync("DELETE FROM parties WHERE id = {0}", party);
        }
    }

    /// <summary>
    /// The party id is the app's to choose when it already has one, which is what the "additive" half
    /// means: the trigger fills a gap, it does not override a caller.
    /// </summary>
    [Fact]
    public async Task An_Explicit_Party_Is_Left_Alone()
    {
        await using var context = CreateContext();
        var userId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var phone = NewPhone();

        try
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO parties (id, party_type, display_name, created_at_utc)
                VALUES ({0}, 'Natural', 'Chosen by the caller', NOW())
                """, partyId);
            await context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO natural_persons (party_id, birthdate, tax_id) VALUES ({0}, NULL, NULL)
                """, partyId);
            await context.Database.ExecuteSqlRawAsync(
                InsertUser, userId, phone, "A", "B", null!, partyId);

            var actual = await context.Database.SqlQuery<Guid?>($"""
                SELECT party_id AS "Value" FROM users WHERE id = {userId}
                """).FirstAsync();

            actual.Should().Be(partyId);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DELETE FROM users WHERE id = {0}", userId);
            await context.Database.ExecuteSqlRawAsync("DELETE FROM natural_persons WHERE party_id = {0}", partyId);
            await context.Database.ExecuteSqlRawAsync("DELETE FROM parties WHERE id = {0}", partyId);
        }
    }

    /// <summary>
    /// A user with no name still gets a party. display_name is NOT NULL and may not be blank,
    /// because a journal line has to print a counterparty - so the phone number is the fallback.
    /// </summary>
    [Fact]
    public async Task A_User_With_No_Name_Falls_Back_To_Their_Phone_Number()
    {
        await using var context = CreateContext();
        var userId = Guid.NewGuid();
        var phone = NewPhone();

        try
        {
            await context.Database.ExecuteSqlRawAsync(
                InsertUser, userId, phone, null!, null!, null!, null!);

            var name = await context.Database.SqlQuery<string>($"""
                SELECT p.display_name AS "Value" FROM users u JOIN parties p ON p.id = u.party_id
                WHERE u.id = {userId}
                """).FirstAsync();

            name.Should().Be(phone);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DELETE FROM natural_persons WHERE party_id IN (SELECT party_id FROM users WHERE id = {0})", userId);
            // The user goes before the party: users.party_id is a foreign key now, and a party a
            // user still points at cannot be deleted. The party id is read off the user BEFORE the
            // user is deleted, because afterwards there is nothing left to read it from.
            var party = await context.Database.SqlQuery<Guid?>($"""
                SELECT party_id AS "Value" FROM users WHERE id = {userId}
                """).FirstAsync();
            await context.Database.ExecuteSqlRawAsync("DELETE FROM users WHERE id = {0}", userId);
            await context.Database.ExecuteSqlRawAsync("DELETE FROM parties WHERE id = {0}", party);
        }
    }

    [Theory]
    [InlineData("12345")]      // too short for an ІПН
    [InlineData("12345678901")] // too long
    [InlineData("12345678a")]   // not digits
    public async Task A_Tax_Id_That_Is_Nine_Or_Ten_Digits_Is_Refused(string taxId)
    {
        await using var context = CreateContext();
        var partyId = Guid.NewGuid();

        try
        {
            var bad = () => context.Database.ExecuteSqlRawAsync("""
                INSERT INTO natural_persons (party_id, birthdate, tax_id) VALUES ({0}, NULL, {1})
                """, partyId, taxId);
            await bad.Should().ThrowAsync<Exception>();
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DELETE FROM natural_persons WHERE party_id = {0}", partyId);
        }
    }

    [Fact]
    public async Task A_Party_Type_Outside_Natural_And_Legal_Is_Refused()
    {
        await using var context = CreateContext();

        var act = () => context.Database.ExecuteSqlRawAsync("""
            INSERT INTO parties (id, party_type, display_name, created_at_utc)
            VALUES ({0}, 'Alien', 'Probe', NOW())
            """, Guid.NewGuid());

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task A_Party_With_No_Printable_Name_Is_Refused()
    {
        await using var context = CreateContext();

        var act = () => context.Database.ExecuteSqlRawAsync("""
            INSERT INTO parties (id, party_type, display_name, created_at_utc)
            VALUES ({0}, 'Natural', '   ', NOW())
            """, Guid.NewGuid());

        await act.Should().ThrowAsync<Exception>();
    }
}
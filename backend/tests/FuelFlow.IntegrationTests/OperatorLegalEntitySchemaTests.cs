using FluentAssertions;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// D-01 - the operator's own legal entity and its bank accounts.
/// <para>
/// Deliberately database-level, like <see cref="DatabaseIntegrityTests"/>. Each test writes through
/// raw SQL and expects the database to refuse it, because the point of these constraints is that
/// they hold when a future handler forgets them - going through EF would only prove EF sent the
/// right string.
/// </para>
/// <para>
/// The two invariants that carry decisions rather than shape are here and nowhere else: at most one
/// active operator entity (single-operator is a decision), and at most one default settlement
/// account per entity. The second is created by raw SQL in the migration, so no model-based test can
/// see it - this asserts it against the live database.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public sealed class OperatorLegalEntitySchemaTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public OperatorLegalEntitySchemaTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    private const string InsertEntity = """
        INSERT INTO operator_legal_entities (id, edrpou, name, tax_regime, is_active, created_at_utc, updated_at_utc)
        VALUES ({0}, {1}, {2}, {3}, {4}, NOW(), NOW())
        """;

    [Theory]
    [InlineData("1234567")]     // 7 digits - too short
    [InlineData("12345678901")] // 11 digits - too long
    [InlineData("1234567a")]    // not digits
    [InlineData("  12345678  ")] // padded: the regex anchors, so whitespace is not digits
    public async Task An_Edrpou_That_Is_Eight_To_Ten_Digits_Is_Refused(string edrpou)
    {
        await using var context = CreateContext();

        var act = () => context.Database.ExecuteSqlRawAsync(
            InsertEntity,
            Guid.NewGuid(), edrpou, "Probe", "Vat", false);

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task A_Nine_Digit_Edrpou_Is_Accepted()
    {
        await using var context = CreateContext();
        var id = Guid.NewGuid();

        var act = () => context.Database.ExecuteSqlRawAsync(
            InsertEntity, id, "123456789", "Probe", "Vat", false);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task A_Tax_Regime_Outside_Vat_And_Simplified_Is_Refused()
    {
        await using var context = CreateContext();

        var act = () => context.Database.ExecuteSqlRawAsync(
            InsertEntity, Guid.NewGuid(), "123456789", "Probe", "single_tax", false);

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task An_Entity_With_A_Non_Twelve_Digit_Vat_Number_Is_Refused()
    {
        await using var context = CreateContext();

        var act = () => context.Database.ExecuteSqlRawAsync("""
            INSERT INTO operator_legal_entities
                (id, edrpou, name, vat_number, tax_regime, is_active, created_at_utc, updated_at_utc)
            VALUES ({0}, {1}, 'Probe', '12345', 'Vat', false, NOW(), NOW())
            """, Guid.NewGuid(), "123456789");

        await act.Should().ThrowAsync<Exception>();
    }

    /// <summary>
    /// Single-operator is the decision the whole money roadmap rests on, so it is an invariant: two
    /// active rows would give a fiscal document two possible issuers.
    /// </summary>
    [Fact]
    public async Task A_Second_Active_Operator_Entity_Is_Refused()
    {
        await using var context = CreateContext();

        await context.Database.ExecuteSqlRawAsync(InsertEntity,
            Guid.NewGuid(), "900000001", "First active", "Vat", true);

        try
        {
            var second = () => context.Database.ExecuteSqlRawAsync(InsertEntity,
                Guid.NewGuid(), "900000002", "Second active", "Vat", true);

            await second.Should().ThrowAsync<Exception>();
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync(
                "DELETE FROM operator_legal_entities WHERE edrpou = '900000001'");
        }
    }

    [Fact]
    public async Task Inactive_Entities_Do_Not_Collide_With_Each_Other()
    {
        await using var context = CreateContext();

        var act = () => context.Database.ExecuteSqlRawAsync(
            InsertEntity, Guid.NewGuid(), "900000003", "Inactive A", "Vat", false);

        // Many draft rows are fine; is_active defaulting to false exists precisely so an entity can
        // be recorded before anyone confirms its identifiers are real.
        await act.Should().NotThrowAsync();

        await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM operator_legal_entities WHERE edrpou = '900000003'");
    }

    /// <summary>
    /// Created by raw SQL in the migration, so this is the only place it is asserted at all.
    /// </summary>
    [Fact]
    public async Task An_Entity_Cannot_Have_Two_Default_Settlement_Accounts()
    {
        await using var context = CreateContext();
        var entityId = Guid.NewGuid();

        await context.Database.ExecuteSqlRawAsync(
            InsertEntity, entityId, "900000010", "Account holder", "Vat", false);

        const string InsertAccount = """
            INSERT INTO bank_accounts (id, operator_legal_entity_id, bank_code, account_ref, currency, is_default, is_active)
            VALUES ({0}, {1}, {2}, {3}, 'UAH', {4}, true)
            """;

        try
        {
            var first = () => context.Database.ExecuteSqlRawAsync(
                InsertAccount, Guid.NewGuid(), entityId, "322", "UA000000001", true);
            await first.Should().NotThrowAsync();

            var second = () => context.Database.ExecuteSqlRawAsync(
                InsertAccount, Guid.NewGuid(), entityId, "322", "UA000000002", true);
            await second.Should().ThrowAsync<Exception>();

            // A non-default account alongside the default is fine - that is the whole point of
            // having a flag rather than a single row.
            var other = () => context.Database.ExecuteSqlRawAsync(
                InsertAccount, Guid.NewGuid(), entityId, "322", "UA000000003", false);
            await other.Should().NotThrowAsync();
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync(
                "DELETE FROM bank_accounts WHERE operator_legal_entity_id = {0}", entityId);
            await context.Database.ExecuteSqlRawAsync(
                "DELETE FROM operator_legal_entities WHERE id = {0}", entityId);
        }
    }

    [Fact]
    public async Task A_Bank_Account_May_Not_Nest_Under_An_Unknown_Entity()
    {
        await using var context = CreateContext();

        var act = () => context.Database.ExecuteSqlRawAsync("""
            INSERT INTO bank_accounts (id, operator_legal_entity_id, bank_code, account_ref, currency, is_default, is_active)
            VALUES ({0}, {1}, '322', 'UA000000009', 'UAH', false, true)
            """, Guid.NewGuid(), Guid.NewGuid());

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task The_Same_Account_Cannot_Be_Registered_Twice_For_One_Entity()
    {
        await using var context = CreateContext();
        var entityId = Guid.NewGuid();

        await context.Database.ExecuteSqlRawAsync(
            InsertEntity, entityId, "900000020", "Duplicate holder", "Vat", false);

        const string InsertAccount = """
            INSERT INTO bank_accounts (id, operator_legal_entity_id, bank_code, account_ref, currency, is_default, is_active)
            VALUES ({0}, {1}, '322', 'UA000000004', 'UAH', false, true)
            """;

        try
        {
            await context.Database.ExecuteSqlRawAsync(InsertAccount, Guid.NewGuid(), entityId);
            var again = () => context.Database.ExecuteSqlRawAsync(
                InsertAccount, Guid.NewGuid(), entityId);

            await again.Should().ThrowAsync<Exception>();
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync(
                "DELETE FROM bank_accounts WHERE operator_legal_entity_id = {0}", entityId);
            await context.Database.ExecuteSqlRawAsync(
                "DELETE FROM operator_legal_entities WHERE id = {0}", entityId);
        }
    }
}
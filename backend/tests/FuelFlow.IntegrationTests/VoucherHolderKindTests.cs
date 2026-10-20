using FluentAssertions;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// D-02 "Consequence - ownership becomes enforceable": <c>holder_kind</c> is a STORED generated
/// column and <c>ck_voucher_holder</c> makes the four-row ownership matrix from
/// <c>MULTI_COMPANY.md</c> §4.3 a legal state rather than a convention.
/// <para>
/// Database-level and written through raw SQL, like <see cref="DatabaseIntegrityTests"/>: the point
/// is that the database refuses an incoherent ownership combination, not that EF reads a column.
/// </para>
/// <para>
/// The disagreement this prevents is not hypothetical. <c>GET /vouchers/my</c> matched the owner on
/// <c>assigned_to_user_id</c> while <c>GET /vouchers/{id}/qr</c> matched
/// <c>worker_user_id ?? assigned_to_user_id</c>, so the owner who had just issued fuel to a worker
/// received the redeemable QR payload from one route and a 403 from the other (#1186).
/// </para>
/// </summary>
[Collection("Integration Tests")]
public sealed class VoucherHolderKindTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public VoucherHolderKindTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    /// <summary>
    /// A company owner plus their company - the two rows every ownership case needs a real reference
    /// for, because assigned_to_user_id, worker_user_id and legal_entity_id all carry foreign keys.
    /// A made-up GUID would be refused by the FK before the constraint under test ever ran.
    /// </summary>
    private async Task<(Guid OwnerId, Guid WorkerId, Guid CompanyId)> SeedPeopleAsync()
    {
        await using var context = CreateContext();
        var owner = Guid.NewGuid();
        var worker = Guid.NewGuid();
        var company = Guid.NewGuid();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO users (id, phone_number, first_name, last_name, created_at_utc, updated_at_utc)
            VALUES ({0}, {1}, 'Owner', 'Probe', now(), now()),
                   ({2}, {3}, 'Worker', 'Probe', now(), now())
            """,
            owner, $"+38050{suffix}1", worker, $"+38050{suffix}2");

        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO legal_entities (id, user_id, name, edrpou, created_at_utc, updated_at_utc)
            VALUES ({0}, {1}, 'Probe Company {2}', {3}, now(), now())
            """, company, owner, suffix, suffix);

        return (owner, worker, company);
    }

    private async Task RemovePeopleAsync(Guid owner, Guid worker, Guid company)
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync("DELETE FROM legal_entities WHERE id = {0}", company);
        await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM users WHERE id IN ({0}, {1})", owner, worker);
    }

    private static string Ref(Guid? id) => id is null ? "NULL" : $"'{id}'::uuid";

    // The fuel id is a uuid, and the mapping is the one the runtime uses - so the fuel this probe
    // names is the same fuel the production database holds.
    private static readonly string InsertVoucher = $$"""
        INSERT INTO fuel_vouchers (id, brand, fuel_type_id, liters, supplier_expiration_date,
                                   customer_expiration_date, voucher_number, qr_payload, qr_payload_sha256,
                                   created_at_utc, updated_at_utc, status, data_kind, is_deleted,
                                   assigned_to_user_id, legal_entity_id, worker_user_id)
        VALUES ('{0}', 'okko', '{{ToUuid("okko-dp")}}', 10, '2027-01-01', '2027-01-01', '{1}',
                convert_to('{2}', 'UTF8'), sha256(convert_to('{2}', 'UTF8')),
                now(), now(), 'Imported', 'real', false,
                {3}, {4}, {5})
        """;

    private async Task<string?> ReadKindForAsync(
        Guid? assignedToUserId, Guid? legalEntityId, Guid? workerUserId, bool expectSuccess = true)
    {
        await using var context = CreateContext();
        var id = Guid.NewGuid();
        var payload = $"holder-probe-{id:N}";

        var sql = string.Format(
            InsertVoucher,
            id,
            $"probe-{id:N}",
            payload,
            Ref(assignedToUserId),
            Ref(legalEntityId),
            Ref(workerUserId));

        var act = () => context.Database.ExecuteSqlRawAsync(sql);

        if (!expectSuccess)
        {
            await act.Should().ThrowAsync<PostgresException>();
            return null;
        }

        await act.Should().NotThrowAsync();

        var kind = await context.Database.SqlQuery<string>($"""
            SELECT holder_kind AS "Value" FROM fuel_vouchers WHERE id = {id}
            """).FirstAsync();

        await context.Database.ExecuteSqlRawAsync("DELETE FROM fuel_vouchers WHERE id = {0}", id);

        return kind;
    }

    [Fact]
    public async Task A_Voucher_Nobody_Holds_Is_Stock()
    {
        var people = await SeedPeopleAsync();
        try
        {
            (await ReadKindForAsync(null, null, null)).Should().Be("Stock");
        }
        finally
        {
            await RemovePeopleAsync(people.OwnerId, people.WorkerId, people.CompanyId);
        }
    }

    /// <summary>
    /// The company's undistributed pool: the fuel belongs to a company but has reached nobody. It
    /// reads Stock, not Company - and that is precisely what ck_voucher_holder pins down by
    /// requiring assigned_to_user_id to be null here.
    /// </summary>
    [Fact]
    public async Task A_Company_Pool_Voucher_Is_Stock_Not_Company()
    {
        var people = await SeedPeopleAsync();
        try
        {
            (await ReadKindForAsync(null, people.CompanyId, null)).Should().Be("Stock");
        }
        finally
        {
            await RemovePeopleAsync(people.OwnerId, people.WorkerId, people.CompanyId);
        }
    }

    [Fact]
    public async Task A_Voucher_Assigned_To_A_Plain_Customer_Is_Personal()
    {
        var people = await SeedPeopleAsync();
        try
        {
            (await ReadKindForAsync(people.OwnerId, null, null)).Should().Be("Personal");
        }
        finally
        {
            await RemovePeopleAsync(people.OwnerId, people.WorkerId, people.CompanyId);
        }
    }

    [Fact]
    public async Task A_Company_Voucher_In_A_Customers_Own_Wallet_Is_Company()
    {
        var people = await SeedPeopleAsync();
        try
        {
            (await ReadKindForAsync(people.OwnerId, people.CompanyId, null)).Should().Be("Company");
        }
        finally
        {
            await RemovePeopleAsync(people.OwnerId, people.WorkerId, people.CompanyId);
        }
    }

    /// <summary>
    /// The row shape that caused #1186: a worker holds the fuel and the owner is still named on the
    /// row. The column must answer Worker, because that is the answer that decides who may spend it.
    /// </summary>
    [Fact]
    public async Task Fuel_Issued_To_A_Worker_Reads_Worker_Even_Though_The_Owner_Is_Still_Named()
    {
        var people = await SeedPeopleAsync();
        try
        {
            (await ReadKindForAsync(people.OwnerId, people.CompanyId, people.WorkerId))
                .Should().Be("Worker");
        }
        finally
        {
            await RemovePeopleAsync(people.OwnerId, people.WorkerId, people.CompanyId);
        }
    }

    /// <summary>
    /// The one combination the matrix forbids: a worker holding fuel with no company behind it. The
    /// fuel would be nobody's to account for - the company's books could not carry it and the
    /// customer's could not either.
    /// </summary>
    [Fact]
    public async Task A_Worker_With_No_Company_Is_Refused()
    {
        var people = await SeedPeopleAsync();
        try
        {
            await ReadKindForAsync(people.OwnerId, null, people.WorkerId, expectSuccess: false);
        }
        finally
        {
            await RemovePeopleAsync(people.OwnerId, people.WorkerId, people.CompanyId);
        }
    }

    /// <summary>
    /// holder_kind is derived, so nothing may write it. A writer that tries is refused by Postgres
    /// itself - which is why this is GENERATED ALWAYS and not a column the application maintains.
    /// </summary>
    [Fact]
    public async Task Holder_Kind_Cannot_Be_Written_Directly()
    {
        var people = await SeedPeopleAsync();
        await using var context = CreateContext();
        var id = Guid.NewGuid();
        var payload = $"holder-write-probe-{id:N}";

        try
        {
            var sql = string.Format(InsertVoucher, id, $"probe-{id:N}", payload,
                "NULL", "NULL", "NULL");
            await context.Database.ExecuteSqlRawAsync(sql);

            var act = () => context.Database.ExecuteSqlRawAsync(
                $"UPDATE fuel_vouchers SET holder_kind = 'Personal' WHERE id = '{id}'");

            await act.Should().ThrowAsync<PostgresException>();
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DELETE FROM fuel_vouchers WHERE id = {0}", id);
            await RemovePeopleAsync(people.OwnerId, people.WorkerId, people.CompanyId);
        }
    }

    /// <summary>
    /// The column has to follow the row it is derived from. A STORED generated column recomputes on
    /// every write, so flipping one ownership column reclassifies the voucher immediately - a
    /// VIRTUAL column would only have recomputed on read.
    /// </summary>
    [Fact]
    public async Task Holder_Kind_Follows_The_Row_When_Ownership_Changes()
    {
        var people = await SeedPeopleAsync();
        await using var context = CreateContext();
        var id = Guid.NewGuid();
        var payload = $"holder-follow-{id:N}";

        try
        {
            var sql = string.Format(InsertVoucher, id, $"probe-{id:N}", payload,
                Ref(people.OwnerId), Ref(people.CompanyId), "NULL");
            await context.Database.ExecuteSqlRawAsync(sql);

            var before = await context.Database.SqlQuery<string>($"""
                SELECT holder_kind AS "Value" FROM fuel_vouchers WHERE id = {id}
                """).FirstAsync();
            before.Should().Be("Company");

            await context.Database.ExecuteSqlRawAsync(
                $"UPDATE fuel_vouchers SET worker_user_id = '{people.WorkerId}' WHERE id = '{id}'");

            var after = await context.Database.SqlQuery<string>($"""
                SELECT holder_kind AS "Value" FROM fuel_vouchers WHERE id = {id}
                """).FirstAsync();
            after.Should().Be("Worker");
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DELETE FROM fuel_vouchers WHERE id = {0}", id);
            await RemovePeopleAsync(people.OwnerId, people.WorkerId, people.CompanyId);
        }
    }

    [Fact]
    public async Task Every_Live_Voucher_Agrees_With_Its_Own_Definition()
    {
        await using var context = CreateContext();

        // ck_voucher_holder makes a violation impossible for new rows. This says the same about what
        // is already there, and would catch a hand-edited CASE in the generated column.
        var inconsistent = await context.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value"
            FROM fuel_vouchers
            WHERE holder_kind <> CASE
                    WHEN worker_user_id IS NOT NULL THEN 'Worker'
                    WHEN legal_entity_id IS NOT NULL AND assigned_to_user_id IS NOT NULL THEN 'Company'
                    WHEN assigned_to_user_id IS NOT NULL THEN 'Personal'
                    ELSE 'Stock'
                  END
            """).FirstAsync();

        inconsistent.Should().Be(0);
    }
}
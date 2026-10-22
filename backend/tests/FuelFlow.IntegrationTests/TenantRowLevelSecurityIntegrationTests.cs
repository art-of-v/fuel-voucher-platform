using FluentAssertions;
using FuelFlow.Features.Company.SharedModels;
using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The row-level policies on the money rows, exercised by the role production connects as.
/// </summary>
/// <remarks>
/// The suite's connection is the container's superuser, and a superuser bypasses row-level
/// security. Every assertion that is about the policies therefore runs after <c>SET ROLE
/// fuelflow_app</c>, which is the role the application uses in production: the same role, the
/// same policies, and the same session variables <c>TenantConnectionInterceptor</c> writes.
/// Tests that only seed rows keep the superuser connection, exactly like a migration does.
/// </remarks>
[Collection("Integration Tests")]
public sealed class TenantRowLevelSecurityIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public TenantRowLevelSecurityIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task The_policies_name_the_traffic_role_and_cover_the_three_tables()
    {
        var expected = new Dictionary<string, string[]>
        {
            ["orders"] = ["tenant_isolation", "tenant_isolation_readonly"],
            ["fuel_vouchers"] =
            [
                "tenant_isolation_read",
                "tenant_isolation_readonly",
                "tenant_isolation_write",
            ],
            ["user_contracts"] = ["tenant_isolation", "tenant_isolation_readonly"],
        };

        await using var connection = await OpenAdminAsync();

        foreach (var (table, policies) in expected)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT c.relrowsecurity,
                       COALESCE(string_agg(p.polname::text, ',' ORDER BY p.polname), ''),
                       COALESCE(string_agg(DISTINCT r.rolname, ','), '')
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                LEFT JOIN pg_policy p ON p.polrelid = c.oid
                LEFT JOIN LATERAL unnest(p.polroles) AS unnested(role_oid) ON true
                LEFT JOIN pg_roles r ON r.oid = role_oid
                WHERE n.nspname = 'public' AND c.relname = @table
                GROUP BY c.relrowsecurity
                """;
            command.Parameters.AddWithValue("table", table);

            await using var reader = await command.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue($"{table} exists");

            reader.GetBoolean(0).Should().BeTrue($"{table} has row-level security enabled");
            reader.GetString(1).Split(',').Should().BeEquivalentTo(policies);
            reader.GetString(2).Split(',').Should().BeEquivalentTo(
                new[] { "fuelflow_app", "fuelflow_readonly" },
                "the traffic role is policed and the reporting role has a read-all policy");
        }
    }

    [Fact]
    public async Task A_customer_sees_only_the_orders_they_placed()
    {
        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();
        var orderA = Guid.NewGuid();
        var orderB = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            seed.Users.AddRange(NewUser(customerA), NewUser(customerB));
            await seed.SaveChangesAsync();

            await InsertOrderAsync(seed, orderA, customerA, legalEntityId: null);
            await InsertOrderAsync(seed, orderB, customerB, legalEntityId: null);
        }

        await using var asA = await OpenAsAsync(customerA);
        (await VisibleOrderIdsAsync(asA, orderA, orderB)).Should().BeEquivalentTo(
            new[] { orderA },
            "an order belongs to the customer who placed it");

        await using var asB = await OpenAsAsync(customerB);
        (await VisibleOrderIdsAsync(asB, orderA, orderB)).Should().BeEquivalentTo(
            new[] { orderB },
            "the other customer's order is not theirs to see");
    }

    [Fact]
    public async Task A_company_owner_and_a_worker_see_the_company_orders_and_an_outsider_does_not()
    {
        var ownerA = Guid.NewGuid();
        var workerA = Guid.NewGuid();
        var ownerB = Guid.NewGuid();
        var legalA = Guid.NewGuid();
        var legalB = Guid.NewGuid();
        var orderA = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            await SeedCompanyAsync(seed, legalA, ownerA, workerA);
            seed.Users.Add(NewUser(ownerB));
            seed.LegalEntities.Add(NewLegalEntity(legalB, ownerB));
            await seed.SaveChangesAsync();

            // Not the owner's own order: without the company arms of the policy it would be
            // invisible to ownerA and workerA, and the test would pass for the wrong reason.
            await InsertOrderAsync(seed, orderA, workerA, legalA);
        }

        await using var asOwner = await OpenAsAsync(ownerA);
        (await VisibleOrderIdsAsync(asOwner, orderA)).Should().ContainSingle(
            "the owner of the legal entity may see its orders");

        await using var asWorker = await OpenAsAsync(workerA);
        (await VisibleOrderIdsAsync(asWorker, orderA)).Should().ContainSingle(
            "a worker may see the orders of the company they work for");

        await using var asOutsider = await OpenAsAsync(ownerB);
        (await VisibleOrderIdsAsync(asOutsider, orderA)).Should().BeEmpty(
            "another company's owner is not a member of this company");
    }

    [Fact]
    public async Task Another_companys_voucher_is_neither_visible_nor_updatable()
    {
        var ownerA = Guid.NewGuid();
        var ownerB = Guid.NewGuid();
        var legalA = Guid.NewGuid();
        var legalB = Guid.NewGuid();
        var voucherB = Guid.NewGuid();
        var voucherNumber = $"RLS-{voucherB:N}";

        await using (var seed = CreateContext())
        {
            seed.Users.AddRange(NewUser(ownerA), NewUser(ownerB));
            seed.LegalEntities.AddRange(
                NewLegalEntity(legalA, ownerA),
                NewLegalEntity(legalB, ownerB));
            await seed.SaveChangesAsync();

            await InsertVoucherAsync(seed, voucherB, legalB, voucherNumber);
        }

        await using var asA = await OpenAsAsync(ownerA);
        (await VisibleVoucherIdsAsync(asA, voucherB)).Should().BeEmpty(
            "the voucher belongs to a company the caller is not part of");

        (await TryRenameVoucherAsync(asA, voucherB)).Should().Be(
            0,
            "an update the policy hides is an update that matched no row");

        await using var asB = await OpenAsAsync(ownerB);
        (await VisibleVoucherIdsAsync(asB, voucherB)).Should().ContainSingle(
            "the owner of the company holding the voucher may see it");

        (await ReadVoucherNumberAsync(voucherB)).Should().Be(
            voucherNumber,
            "no caller may rewrite a hidden row into a visible one");
    }

    [Fact]
    public async Task Unassigned_stock_is_readable_but_not_writable_by_a_customer()
    {
        var customer = Guid.NewGuid();
        var voucher = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            seed.Users.Add(NewUser(customer));
            await seed.SaveChangesAsync();

            await InsertVoucherAsync(seed, voucher, legalEntityId: null, voucherNumber: $"STOCK-{voucher:N}");
        }

        await using var asCustomer = await OpenAsAsync(customer);
        (await VisibleVoucherIdsAsync(asCustomer, voucher)).Should().ContainSingle(
            "checkout has to be able to ask whether the stock it is selling exists");

        (await TryRenameVoucherAsync(asCustomer, voucher)).Should().Be(
            0,
            "reading the public pool is not the right to modify it");
    }

    [Fact]
    public async Task A_signed_contract_is_visible_only_to_its_signer()
    {
        var signerA = Guid.NewGuid();
        var signerB = Guid.NewGuid();
        var legalA = Guid.NewGuid();
        var userContractA = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            seed.Users.AddRange(NewUser(signerA), NewUser(signerB));
            seed.LegalEntities.Add(NewLegalEntity(legalA, signerA));
            await seed.SaveChangesAsync();

            var (brandId, _, _) = await ReadFirstCatalogueEntryAsync();
            await InsertUserContractAsync(seed, userContractA, signerA, legalA, brandId);
        }

        await using var asA = await OpenAsAsync(signerA);
        (await VisibleContractIdsAsync(asA, userContractA)).Should().ContainSingle(
            "the customer who signed the contract may read it");

        await using var asB = await OpenAsAsync(signerB);
        (await VisibleContractIdsAsync(asB, userContractA)).Should().BeEmpty(
            "a signed contract is the signer's, not every customer's");
    }

    [Fact]
    public async Task Bypassing_the_model_filters_does_not_bypass_the_policies()
    {
        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();
        var liveOrderA = Guid.NewGuid();
        var deletedOrderB = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            seed.Users.AddRange(NewUser(customerA), NewUser(customerB));
            await seed.SaveChangesAsync();

            await InsertOrderAsync(seed, liveOrderA, customerA, legalEntityId: null);
            await InsertOrderAsync(seed, deletedOrderB, customerB, legalEntityId: null, isDeleted: true);
        }

        await using var connection = await OpenAsAsync(customerA);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connection)
            .Options;

        await using var context = new ApplicationDbContext(options, TestQrCipher.Instance);

        var visible = await context.Orders
            .IncludingSoftDeleted()
            .Select(o => o.Id)
            .ToListAsync();

        visible.Should().ContainSingle()
            .Which.Should().Be(liveOrderA,
                "the soft-delete scope lifts the EF filter, not the row-level policy: another " +
                "tenant's deleted order stays invisible");
    }

    [Fact]
    public async Task The_system_scope_reads_across_tenants()
    {
        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();
        var orderA = Guid.NewGuid();
        var orderB = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            seed.Users.AddRange(NewUser(customerA), NewUser(customerB));
            await seed.SaveChangesAsync();

            await InsertOrderAsync(seed, orderA, customerA, legalEntityId: null);
            await InsertOrderAsync(seed, orderB, customerB, legalEntityId: null);
        }

        await using var system = await OpenAsAsync(userId: null, system: true);
        (await VisibleOrderIdsAsync(system, orderA, orderB)).Should().BeEquivalentTo(
            new[] { orderA, orderB },
            "webhooks, background jobs and the staff surfaces legitimately act across tenants");
    }

    [Fact]
    public async Task The_readonly_reporting_role_reads_across_tenants()
    {
        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();
        var orderA = Guid.NewGuid();
        var orderB = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            seed.Users.AddRange(NewUser(customerA), NewUser(customerB));
            await seed.SaveChangesAsync();

            await InsertOrderAsync(seed, orderA, customerA, legalEntityId: null);
            await InsertOrderAsync(seed, orderB, customerB, legalEntityId: null);
        }

        await using var reporting = await OpenAsReadonlyAsync();
        (await VisibleOrderIdsAsync(reporting, orderA, orderB)).Should().BeEquivalentTo(
            new[] { orderA, orderB },
            "the optional reporting role exists to read across tenants, and row-level security is default-deny");
    }

    [Fact]
    public async Task The_host_writes_the_system_scope_before_the_first_statement()
    {
        var accessor = new TenantScopeAccessor(new HttpContextAccessor());
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .AddInterceptors(new TenantConnectionInterceptor(accessor))
            .Options;

        await using var context = new ApplicationDbContext(options, TestQrCipher.Instance);

        var isSystem = await context.Database
            .SqlQueryRaw<string>("SELECT current_setting('app.is_system', true) AS \"Value\"")
            .SingleAsync();

        isSystem.Should().Be(
            "on",
            "a unit of work with no HTTP request is the system, and the interceptor must say so");
    }

    private ApplicationDbContext CreateContext()
        => new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(_fixture.ConnectionString)
                .Options,
            TestQrCipher.Instance);

    private NpgsqlConnection NewConnection()
        => new(
            new NpgsqlConnectionStringBuilder(_fixture.ConnectionString)
            {
                Pooling = false,
            }.ConnectionString);

    private async Task<NpgsqlConnection> OpenAdminAsync()
    {
        var connection = NewConnection();
        await connection.OpenAsync();
        return connection;
    }

    private async Task<NpgsqlConnection> OpenAsAsync(Guid? userId, bool system = false)
    {
        var connection = await OpenAdminAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SET ROLE fuelflow_app;
            SELECT set_config('app.user_id', @user_id, false),
                   set_config('app.is_system', @is_system, false);
            """;
        command.Parameters.AddWithValue("user_id", userId?.ToString("D") ?? "");
        command.Parameters.AddWithValue("is_system", system ? "on" : "off");
        await command.ExecuteNonQueryAsync();

        return connection;
    }

    private async Task<NpgsqlConnection> OpenAsReadonlyAsync()
    {
        var connection = await OpenAdminAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SET ROLE fuelflow_readonly;";
        await command.ExecuteNonQueryAsync();

        return connection;
    }

    private static async Task SeedCompanyAsync(
        ApplicationDbContext context,
        Guid legalEntityId,
        Guid ownerId,
        Guid workerId)
    {
        context.Users.AddRange(NewUser(ownerId), NewUser(workerId));
        context.LegalEntities.Add(NewLegalEntity(legalEntityId, ownerId));
        context.CompanyMembers.Add(new CompanyMember
        {
            Id = Guid.NewGuid(),
            LegalEntityId = legalEntityId,
            WorkerUserId = workerId,
            JoinedAtUtc = TestClock.UtcNow,
        });

        await context.SaveChangesAsync();
    }

    private static async Task InsertOrderAsync(
        ApplicationDbContext context,
        Guid id,
        Guid userId,
        Guid? legalEntityId,
        bool isDeleted = false)
    {
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO public.orders
                (id, user_id, legal_entity_id, price, status, is_deleted, created_at_utc, updated_at_utc)
            VALUES
                (@id, @user_id, @legal_entity_id, 100, 'Paid', @is_deleted, now(), now())
            """,
            new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = id },
            new NpgsqlParameter("user_id", NpgsqlDbType.Uuid) { Value = userId },
            new NpgsqlParameter("legal_entity_id", NpgsqlDbType.Uuid)
            {
                Value = legalEntityId ?? (object)DBNull.Value,
            },
            new NpgsqlParameter("is_deleted", NpgsqlDbType.Boolean) { Value = isDeleted });
    }

    private async Task InsertVoucherAsync(
        ApplicationDbContext context,
        Guid id,
        Guid? legalEntityId,
        string voucherNumber)
    {
        var (brandId, brand, fuelTypeId) = await ReadFirstCatalogueEntryAsync();

        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO public.fuel_vouchers
                (id, brand, brand_id, fuel_type_id, liters,
                 supplier_expiration_date, customer_expiration_date, voucher_number,
                 qr_payload, qr_payload_sha256, created_at_utc, status, data_kind,
                 legal_entity_id, updated_at_utc)
            VALUES
                (@id, @brand, @brand_id, @fuel_type_id, 1,
                 CURRENT_DATE + 30, CURRENT_DATE + 30, @voucher_number,
                 '\x00'::bytea, decode(md5(@hash_seed), 'hex'), now(), 'Available', 'real',
                 @legal_entity_id, now())
            """,
            new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = id },
            new NpgsqlParameter("brand", NpgsqlDbType.Text) { Value = brand },
            new NpgsqlParameter("brand_id", NpgsqlDbType.Uuid) { Value = brandId },
            new NpgsqlParameter("fuel_type_id", NpgsqlDbType.Uuid) { Value = fuelTypeId },
            new NpgsqlParameter("voucher_number", NpgsqlDbType.Text) { Value = voucherNumber },
            new NpgsqlParameter("hash_seed", NpgsqlDbType.Text) { Value = voucherNumber },
            new NpgsqlParameter("legal_entity_id", NpgsqlDbType.Uuid)
            {
                Value = legalEntityId ?? (object)DBNull.Value,
            });
    }

    private static async Task InsertUserContractAsync(
        ApplicationDbContext context,
        Guid id,
        Guid userId,
        Guid legalEntityId,
        Guid brandId)
    {
        var contractId = Guid.NewGuid();

        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO public.contracts
                (id, brand_id, legal_entity_id, user_id, created_at_utc, expires_at_utc, terms_version)
            VALUES
                (@contract_id, @brand_id, @legal_entity_id, @user_id, now(), now() + interval '1 year', 1);

            INSERT INTO public.user_contracts
                (id, user_id, contract_id, signed_at_utc, terms_version)
            VALUES
                (@id, @user_id, @contract_id, now(), 1);
            """,
            new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = id },
            new NpgsqlParameter("contract_id", NpgsqlDbType.Uuid) { Value = contractId },
            new NpgsqlParameter("brand_id", NpgsqlDbType.Uuid) { Value = brandId },
            new NpgsqlParameter("legal_entity_id", NpgsqlDbType.Uuid) { Value = legalEntityId },
            new NpgsqlParameter("user_id", NpgsqlDbType.Uuid) { Value = userId });
    }

    private async Task<(Guid BrandId, string Brand, Guid FuelTypeId)> ReadFirstCatalogueEntryAsync()
    {
        await using var connection = await OpenAdminAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT b.id, b.slug, ft.id
            FROM public.fuel_types ft
            JOIN public.brands b ON b.id = ft.brand_id
            WHERE ft.is_deleted = false
            ORDER BY ft.id
            LIMIT 1
            """;

        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue(
            "the migrations seed the fuel catalogue this test borrows a brand and grade from");

        return (reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2));
    }

    private static async Task<Guid[]> VisibleOrderIdsAsync(
        NpgsqlConnection connection,
        params Guid[] ids)
        => await VisibleIdsAsync(connection, "orders", ids);

    private static async Task<Guid[]> VisibleVoucherIdsAsync(
        NpgsqlConnection connection,
        params Guid[] ids)
        => await VisibleIdsAsync(connection, "fuel_vouchers", ids);

    private static async Task<Guid[]> VisibleContractIdsAsync(
        NpgsqlConnection connection,
        params Guid[] ids)
        => await VisibleIdsAsync(connection, "user_contracts", ids);

    private static async Task<Guid[]> VisibleIdsAsync(
        NpgsqlConnection connection,
        string table,
        params Guid[] ids)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = table switch
        {
            "orders" => """SELECT id FROM public.orders WHERE id = ANY(@ids) ORDER BY id""",
            "fuel_vouchers" => """SELECT id FROM public.fuel_vouchers WHERE id = ANY(@ids) ORDER BY id""",
            "user_contracts" => """SELECT id FROM public.user_contracts WHERE id = ANY(@ids) ORDER BY id""",
            _ => throw new ArgumentOutOfRangeException(nameof(table), table, "Not a protected table."),
        };
        command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
        {
            Value = ids,
        });

        var visible = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            visible.Add(reader.GetGuid(0));

        return visible.ToArray();
    }

    private static async Task<int> TryRenameVoucherAsync(NpgsqlConnection connection, Guid id)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE public.fuel_vouchers
               SET voucher_number = voucher_number || '-changed',
                   updated_at_utc = now()
             WHERE id = @id
            """;
        command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = id });
        return await command.ExecuteNonQueryAsync();
    }

    private async Task<string> ReadVoucherNumberAsync(Guid id)
    {
        await using var connection = await OpenAdminAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT voucher_number FROM public.fuel_vouchers WHERE id = @id";
        command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = id });

        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static User NewUser(Guid id) => new()
    {
        Id = id,
        PhoneNumber = $"+38{id:N}"[..20],
        IsActive = true,
        CreatedAtUtc = TestClock.UtcNow,
        UpdatedAtUtc = TestClock.UtcNow,
    };

    private static LegalEntity NewLegalEntity(Guid id, Guid ownerUserId) => new()
    {
        Id = id,
        UserId = ownerUserId,
        Name = $"RLS Test {id:N}"[..40],
        Edrpou = id.ToString("N")[..8],
        CreatedAtUtc = TestClock.UtcNow,
        UpdatedAtUtc = TestClock.UtcNow,
    };
}

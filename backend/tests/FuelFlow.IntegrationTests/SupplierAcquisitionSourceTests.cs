using FluentAssertions;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Real-Postgres coverage for <c>suppliers.acquisition_source</c> — the route a supplier sells on,
/// which the import copies onto every voucher it creates.
/// <para>
/// The property that matters is that the two cannot drift apart. Vouchers hold a copy, not a join,
/// precisely so a voucher's cost stays in the bucket it was bought into; that only holds while the
/// supplier it was copied from is itself unchangeable. A supplier that can be reclassified would let
/// a correction made today retroactively disagree with vouchers imported months ago, with both columns
/// individually valid and nothing reporting the disagreement.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public class SupplierAcquisitionSourceTests : WebApplicationFactory<Program>, IClassFixture<TestDatabaseFixture>
{
    /// <summary>
    /// Why the rollback test below is skipped.
    /// </summary>
    /// <remarks>
    /// <c>fuel_offer_volumes</c> was dropped on 2026-10-18. Four older migrations seed that table
    /// through <c>HasData</c>, and EF resolves a data operation's column mappings from the CURRENT
    /// model - which no longer maps the table. Rolling back past the drop and forward again replays
    /// those <c>InsertData</c> calls and throws. The migration under test is unaffected; only the
    /// ability to walk the schema back across the drop is gone.
    /// </remarks>
    internal const string SkippedBecauseVolumesAreGone =
        "fuel_offer_volumes was dropped on 2026-10-18; four older migrations seed it through HasData and EF builds a data operation's column mappings from the current model, so rolling forward again replays them and throws. The migration under test is fine - only the rollback is.";
    public SupplierAcquisitionSourceTests(TestDatabaseFixture fixture) => _ = fixture;

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseEnvironment("Development");

    private ApplicationDbContext NewContext()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    }

    /// <summary>
    /// Inserts a supplier without stating a route, which is what every existing supplier-creation path
    /// does and what an operator adding one by hand will keep doing.
    /// </summary>
    private async Task<Guid> InsertSupplierAsync(string? statedSource = null)
    {
        using var context = NewContext();

        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            Name = $"acq-test-{Guid.NewGuid():N}",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            AcquisitionSource = statedSource ?? AcquisitionSources.Default,
        };

        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();
        return supplier.Id;
    }

    private async Task<string> ReadAsync(Guid supplierId)
    {
        using var context = NewContext();
        return (await context.Suppliers
            .AsNoTracking()
            .Where(s => s.Id == supplierId)
            .Select(s => s.AcquisitionSource)
            .SingleAsync())!;
    }

    // --------------------------------------------------------------------------------------------
    // The default, and the stated value
    // --------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_supplier_that_never_states_a_route_is_direct()
    {
        // Every supplier in the database today sells on that route, which is what makes the default a
        // statement of fact and lets the column be NOT NULL without the supplier form having to think
        // about routes at all.
        var supplierId = await InsertSupplierAsync();

        (await ReadAsync(supplierId)).Should().Be(AcquisitionSources.Direct);
    }

    [Fact]
    public async Task A_stated_partner_route_survives_the_insert()
    {
        var supplierId = await InsertSupplierAsync(AcquisitionSources.Partner);

        (await ReadAsync(supplierId)).Should().Be(AcquisitionSources.Partner);
    }

    [Fact]
    public async Task A_route_outside_the_set_is_rejected_by_the_database()
    {
        using var context = NewContext();

        context.Suppliers.Add(new Supplier
        {
            Id = Guid.NewGuid(),
            Name = $"acq-bad-{Guid.NewGuid():N}",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            AcquisitionSource = "Partner",
        });

        // Capitalised. The CHECK is case-sensitive, and this is exactly the typo an operator or a
        // script can make - and without the constraint it would be a supplier whose vouchers are
        // imported into a bucket nothing reports.
        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    // --------------------------------------------------------------------------------------------
    // Immutability
    // --------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_supplier_cannot_be_moved_between_routes()
    {
        var supplierId = await InsertSupplierAsync(AcquisitionSources.Partner);
        using var context = NewContext();

        var act = async () => await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE suppliers SET acquisition_source = 'direct' WHERE id = {supplierId}");

        await act.Should().ThrowAsync<Exception>()
            .WithMessage("*never changes*",
                "every voucher bought from this supplier holds a copy of the route, so an edit would "
                + "retroactively disagree with them while both columns stayed individually valid");

        (await ReadAsync(supplierId)).Should().Be(AcquisitionSources.Partner);
    }

    [Fact]
    public async Task Editing_a_suppliers_requisites_still_works()
    {
        // The guard must not become "this table cannot be updated". Requisites are corrected in the
        // ordinary course of business - a new phone number, a fixed address - and that is the most
        // common write on this row.
        var supplierId = await InsertSupplierAsync(AcquisitionSources.Partner);
        using var context = NewContext();

        var act = async () =>
        {
            var supplier = await context.Suppliers.FirstAsync(s => s.Id == supplierId);
            supplier.Phone = "+380501234567";
            supplier.UpdatedAtUtc = DateTime.UtcNow;
            await context.SaveChangesAsync();
        };

        await act.Should().NotThrowAsync("immutable means immutable, not frozen");

        using var verify = NewContext();
        var supplier = await verify.Suppliers.AsNoTracking().FirstAsync(s => s.Id == supplierId);
        supplier.Phone.Should().Be("+380501234567");
        supplier.AcquisitionSource.Should().Be(AcquisitionSources.Partner);
    }

    [Fact]
    public async Task The_column_is_not_null_in_the_database()
    {
        using var context = NewContext();

        var isNullable = await context.Database
            .SqlQueryRaw<string>(
                """
                SELECT is_nullable
                FROM information_schema.columns
                WHERE table_name = 'suppliers'
                  AND column_name = 'acquisition_source'
                """)
            .ToListAsync();

        isNullable.Should().ContainSingle().Which.Should().Be("NO");
    }

    // --------------------------------------------------------------------------------------------
    // The migration itself
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Rehearsed against suppliers that already exist. A fresh test container builds its schema by
    /// running migrations, so the default is otherwise only ever applied to nothing — and a deploy is
    /// exactly when the back-fill meets real rows. <c>MigrateDatabaseOnStartup</c> rethrows, so a
    /// mistake there is a crash loop rather than a warning.
    /// </summary>
    [Fact(Skip = SkippedBecauseVolumesAreGone)]
    public async Task The_migration_backfills_suppliers_that_already_existed()
    {
        const string BeforeThisMigration = "20261018092607_VoucherAcquisitionSource";

        using var context = NewContext();
        var migrator = context.Database.GetService<IMigrator>();

        Guid preExisting;

        try
        {
            await migrator.MigrateAsync(BeforeThisMigration);

            preExisting = Guid.NewGuid();
            var name = "acq-pre-" + Guid.NewGuid().ToString("N");

            // Raw SQL: EF's model already carries the column, so an EF insert at the old schema would
            // ask for a column that does not exist yet - not the situation under test.
#pragma warning disable EF1002
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO suppliers (id, name, is_active, created_at_utc, updated_at_utc)
                VALUES ({preExisting}, {name}, true, now(), now())
                """);
#pragma warning restore EF1002

            await migrator.MigrateAsync();

            using var after = NewContext();
            var stored = await after.Database
                .SqlQueryRaw<string>(
                    $"SELECT acquisition_source FROM suppliers WHERE id = '{preExisting}'")
                .ToListAsync();

            stored.Should().ContainSingle().Which.Should().Be(AcquisitionSources.Direct);
        }
        finally
        {
            await migrator.MigrateAsync();
        }
    }
}

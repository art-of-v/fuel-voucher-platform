using System.Text.Json;
using FluentAssertions;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.PurchaseBatchCost;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The per-litre cost entered by hand at import is the only record we keep of what we paid — there is
/// no supplier invoice to fall back on, so this is the sole trace of the money.
/// <para>
/// It runs against the real Postgres container on purpose: <c>old_value</c>/<c>new_value</c> are
/// <c>jsonb</c> columns, and the in-memory provider behind the unit tests accepts anything handed to
/// it. Only Postgres rejects a value that is not valid JSON — exactly the class of defect a unit test
/// cannot see.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public sealed class CostEntryAuditIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public CostEntryAuditIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.DbContainer.GetConnectionString())
            // Matches production: the API DbContext is read-only for queries and the handler
            // re-attaches explicitly. Running tracking here would hide a missing Update call.
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options);

    private static SetBatchCostCommandHandler Handler(ApplicationDbContext ctx)
        => new(ctx, new BlendedCostRecalculator(ctx), new ProviderEventService(ctx), NotificationDispatcher.Disabled);

    /// <summary>One import holding one uncosted voucher of a fuel the migrations already seed.</summary>
    private static async Task<Guid> SeedUncostedImportAsync(ApplicationDbContext context, string suffix)
    {
        var import = new VoucherImport
        {
            Id = Guid.NewGuid(),
            FileName = $"cost-audit-{suffix}.pdf",
            PageCount = 1,
            StartedAtUtc = DateTime.UtcNow,
            CompletedAtUtc = DateTime.UtcNow,
            Status = "Completed",
            ImportedCount = 1,
        };
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            Name = $"Audit supplier {suffix}",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        context.VoucherImports.Add(import);
        context.Suppliers.Add(supplier);
        context.FuelVouchers.Add(new FuelVoucher
        {
            Id = Guid.NewGuid(),
            ImportJobId = import.Id,
            SupplierId = supplier.Id,
            Provider = "okko",
            FuelTypeId = "okko-95",
            Liters = 50,
            VoucherNumber = $"audit-{suffix}",
            QrPayload = $"9015$2000$;{suffix}=x?",
            Status = VoucherStatus.Imported,
            ProviderExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(180),
            CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(180),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });

        await context.SaveChangesAsync();
        return import.Id;
    }

    [Fact]
    public async Task SetBatchCost_ShouldWriteAnAuditRow_RecordingWhoEnteredWhat()
    {
        using var setup = CreateContext();
        var importId = await SeedUncostedImportAsync(setup, "audit");
        var actor = Guid.NewGuid();

        using var ctx = CreateContext();
        var result = await Handler(ctx).HandleAsync(
            new SetBatchCostCommand(importId, "okko-95", 49.50m, actor, "Cost Tester"));
        result.Success.Should().BeTrue();

        using var verify = CreateContext();
        var row = await verify.Set<ProviderEventOutbox>().AsNoTracking()
            .Where(e => e.EventType == "BatchCostEntered" && e.AggregateId == $"{importId}:okko-95")
            .SingleOrDefaultAsync();

        // With no invoice, this row IS the record of what we paid. If it is missing, the money that
        // left the company cannot be reconstructed at all.
        row.Should().NotBeNull();
        row!.ChangedByUserId.Should().Be(actor);
        row.ChangedByUserName.Should().Be("Cost Tester");
        row.Summary.Should().Contain("49.50");
    }

    [Fact]
    public async Task SetBatchCost_ShouldStoreTheCostAsJson_NotAsABareDecimalString()
    {
        using var setup = CreateContext();
        var importId = await SeedUncostedImportAsync(setup, "json");

        using (var ctx = CreateContext())
        {
            var result = await Handler(ctx).HandleAsync(
                new SetBatchCostCommand(importId, "okko-95", 51.25m, Guid.NewGuid(), "JSON Tester"));
            result.Success.Should().BeTrue();
        }

        using var verify = CreateContext();
        var row = await verify.Set<ProviderEventOutbox>().AsNoTracking()
            .Where(e => e.EventType == "BatchCostEntered" && e.AggregateId == $"{importId}:okko-95")
            .SingleAsync();

        // A bare "51.25" is not valid JSON and the provider history cannot render it — hence an
        // assertion on parse, not on a shape someone chose in advance.
        var act = () => JsonDocument.Parse(row.NewValue);
        act.Should().NotThrow("old_value/new_value are jsonb: they must hold JSON, never a bare string");

        using var doc = JsonDocument.Parse(row.NewValue);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Object,
            "every other audit event stores an object, so this one has to be readable the same way");
        doc.RootElement.GetProperty("CostPerLiter").GetDecimal().Should().Be(51.25m);
        doc.RootElement.GetProperty("FuelTypeId").GetString().Should().Be("okko-95");
    }

    [Fact]
    public async Task SetBatchCost_ShouldAuditAForcedOverwrite_WithBothTheOldAndTheNewCost()
    {
        using var setup = CreateContext();
        var importId = await SeedUncostedImportAsync(setup, "overwrite");

        using (var first = CreateContext())
        {
            await Handler(first).HandleAsync(
                new SetBatchCostCommand(importId, "okko-95", 49.50m, Guid.NewGuid(), "First"));
        }
        using (var second = CreateContext())
        {
            var result = await Handler(second).HandleAsync(
                new SetBatchCostCommand(importId, "okko-95", 47.00m, Guid.NewGuid(), "Second", ForceOverwrite: true));
            result.Success.Should().BeTrue();
        }

        using var verify = CreateContext();
        var rows = await verify.Set<ProviderEventOutbox>().AsNoTracking()
            .Where(e => e.EventType == "BatchCostEntered" && e.AggregateId == $"{importId}:okko-95")
            .OrderBy(e => e.ChangedAtUtc)
            .ToListAsync();

        rows.Should().HaveCount(2, "correcting a cost is exactly the change that must be traceable");
        using var latest = JsonDocument.Parse(rows[^1].NewValue);
        latest.RootElement.GetProperty("CostPerLiter").GetDecimal().Should().Be(47.00m);
        rows[^1].OldValue.Should().NotBeNull("the overwritten value is the whole point of an audit row");
        using var oldDoc = JsonDocument.Parse(rows[^1].OldValue!);
        oldDoc.RootElement.GetProperty("CostPerLiter").GetDecimal().Should().Be(49.50m);
    }
}
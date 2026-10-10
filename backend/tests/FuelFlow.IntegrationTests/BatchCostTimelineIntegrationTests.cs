using FluentAssertions;
using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Exchange;
using FuelFlow.Features.Vouchers.PurchaseBatchCost;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The per-batch costing timeline (#178) against a real PostgreSQL database.
/// </summary>
/// <remarks>
/// The point of the feature is auditability: an operator must be able to see which exchanges took
/// stock out of a batch and what that cost. Two things are easy to get wrong and are pinned here -
/// the surcharge is replicated onto every row of an exchange, so a naive sum multiplies the money, and
/// an exchange must not appear in the timeline of a batch whose stock it did not touch.
/// </remarks>
[Collection("Integration Tests")]
public sealed class BatchCostTimelineIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private static readonly Guid SupplierId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly TestDatabaseFixture _fixture;

    public BatchCostTimelineIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Timeline_ShowsAnExchangeThatTookStockOutOfTheBatch()
    {
        var importId = Guid.NewGuid();
        var oldVouchers = Enumerable.Range(0, 3)
            .Select(_ => NewVoucher(importId, 10m, 40m))
            .ToList();
        var newVouchers = Enumerable.Range(0, 3)
            .Select(_ => NewVoucher(importId, 10m, 50m))
            .ToList();
        var exchangeBatchId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            await SeedImportAndFuel(seed, importId);
            seed.FuelVouchers.AddRange(oldVouchers);
            seed.FuelVouchers.AddRange(newVouchers);
            // One row per retired voucher, each carrying the SAME batch total - the trap.
            for (var i = 0; i < oldVouchers.Count; i++)
            {
                seed.VoucherExchanges.Add(new VoucherExchange
                {
                    Id = Guid.NewGuid(),
                    ExchangeBatchId = exchangeBatchId,
                    OldVoucherId = oldVouchers[i].Id,
                    NewVoucherId = newVouchers[i].Id,
                    FuelTypeId = "okko-dp",
                    Provider = "OKKO",
                    SupplierId = SupplierId,
                    SurchargeUah = 300m,
                    CostPerLiterApplied = 50m,
                    InvoiceNumber = "INV-1",
                    CreatedAtUtc = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc),
                });
            }
            await seed.SaveChangesAsync();
        }

        var timeline = await TimelineAsync(importId);

        var entry = timeline.Entries.Should().ContainSingle().Subject;
        entry.ExchangeBatchId.Should().Be(exchangeBatchId);
        // 300 UAH total, NOT 900 (300 x 3 rows).
        entry.SurchargeUah.Should().Be(300m);
        entry.VouchersExchangedOut.Should().Be(3);
        entry.LitersExchangedOut.Should().Be(30m);
        entry.CostPerLiterBefore.Should().Be(40m);
        entry.CostPerLiterAfter.Should().Be(50m);
        entry.InvoiceNumber.Should().Be("INV-1");
    }

    [Fact]
    public async Task Timeline_DoesNotShowAnExchangeOfAnotherBatchesStock()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var exchangeBatchId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            await SeedImportAndFuelExtra(seed, theirs);
            await SeedImportAndFuel(seed, mine);

            var mineVoucher = NewVoucher(mine, 10m, 40m);
            var theirVoucher = NewVoucher(theirs, 10m, 40m);
            seed.FuelVouchers.AddRange(mineVoucher, theirVoucher);
            seed.VoucherExchanges.Add(new VoucherExchange
            {
                Id = Guid.NewGuid(),
                ExchangeBatchId = exchangeBatchId,
                OldVoucherId = theirVoucher.Id,
                FuelTypeId = "okko-dp",
                Provider = "OKKO",
                    SupplierId = SupplierId,
                SurchargeUah = 300m,
                CostPerLiterApplied = 50m,
                CreatedAtUtc = DateTime.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        (await TimelineAsync(mine)).Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Timeline_OrdersEntriesOldestFirst_AndKeepsThemApart()
    {
        var importId = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            await SeedImportAndFuel(seed, importId);
            var a = NewVoucher(importId, 10m, 40m);
            var b = NewVoucher(importId, 10m, 40m);
            seed.FuelVouchers.AddRange(a, b);
            seed.VoucherExchanges.Add(new VoucherExchange
            {
                Id = Guid.NewGuid(),
                ExchangeBatchId = second,
                OldVoucherId = b.Id,
                FuelTypeId = "okko-dp",
                Provider = "OKKO",
                    SupplierId = SupplierId,
                SurchargeUah = 200m,
                CostPerLiterApplied = 45m,
                CreatedAtUtc = new DateTime(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc),
            });
            seed.VoucherExchanges.Add(new VoucherExchange
            {
                Id = Guid.NewGuid(),
                ExchangeBatchId = first,
                OldVoucherId = a.Id,
                FuelTypeId = "okko-dp",
                Provider = "OKKO",
                    SupplierId = SupplierId,
                SurchargeUah = 100m,
                CostPerLiterApplied = 42m,
                CreatedAtUtc = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc),
            });
            await seed.SaveChangesAsync();
        }

        var timeline = await TimelineAsync(importId);

        timeline.Entries.Should().HaveCount(2);
        timeline.Entries[0].ExchangeBatchId.Should().Be(first, "an audit timeline reads forwards in time");
        timeline.Entries[1].ExchangeBatchId.Should().Be(second);
    }

    [Fact]
    public async Task Timeline_ForABatchWithNoExchanges_IsEmptyButStillReportsCurrentCost()
    {
        var importId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            await SeedImportAndFuel(seed, importId);
            seed.FuelVouchers.AddRange(NewVoucher(importId, 10m, 40m), NewVoucher(importId, 20m, 60m));
            await seed.SaveChangesAsync();
        }

        var timeline = await TimelineAsync(importId);

        timeline.Entries.Should().BeEmpty();
        // 10 L @ 40 + 20 L @ 60 = 1600 UAH over 30 L = 53.33 per litre; the batch still reports
        // what it costs now.
        var current = timeline.CurrentCosts.Should().ContainSingle().Subject;
        current.CostPerLiter.Should().BeApproximately(53.333m, 0.01m);
    }

    private async Task<ImportBatchCostTimeline> TimelineAsync(Guid importId)
    {
        var context = CreateContext();
        var handler = new GetImportBatchCostTimelineQueryHandler(
            context,
            new GetImportBatchCostsQueryHandler(context, new BlendedCostRecalculator(context)));
        return await handler.HandleAsync(new GetImportBatchCostTimelineQuery(importId));
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.DbContainer.GetConnectionString())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options);

    private static async Task ResetAsync(ApplicationDbContext context)
        => await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "refunds", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "voucher_exchanges", "company_invitations", "company_members", "legal_entities", "verification_codes", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");

    private static async Task SeedImportAndFuel(ApplicationDbContext seed, Guid importId)
    {
        // fuel_vouchers.import_job_id is an FK to voucher_imports, and supplier_id to suppliers;
        // real Postgres enforces both.
        seed.VoucherImports.Add(new VoucherImport
        {
            Id = importId,
            FileName = "timeline.pdf",
            Status = "Completed",
            StartedAtUtc = DateTime.UtcNow
        });

        if (!await seed.Suppliers.AnyAsync(s => s.Id == SupplierId))
        {
            seed.Suppliers.Add(new Supplier
            {
                Id = SupplierId,
                Name = "Timeline Supplier",
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }

        // fuel_types is reference data that other suites in this shared container also seed, and it is
        // not truncated here - so add it only when absent rather than colliding on the primary key.
        if (!await seed.FuelTypes.AnyAsync(f => f.Id == "okko-dp"))
        {
            seed.FuelTypes.Add(new FuelTypeEntity
            {
                Id = "okko-dp",
                Name = "ДП ЄВРО",
                StationId = "okko",
                BasePrice = 60m,
                DiscountPrice = 60m,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }
    }

    /// <summary>The second import of the cross-import test: another parent row, no fuel reference data.</summary>
    private static Task SeedImportAndFuelExtra(ApplicationDbContext seed, Guid importId)
    {
        seed.VoucherImports.Add(new VoucherImport
        {
            Id = importId,
            FileName = "other.pdf",
            Status = "Completed",
            StartedAtUtc = DateTime.UtcNow
        });
        return Task.CompletedTask;
    }
    private static FuelVoucher NewVoucher(Guid importId, decimal liters, decimal? costPerLiter)
        => new()
        {
            Id = Guid.NewGuid(),
            Provider = "OKKO",
            FuelTypeId = "okko-dp",
            Liters = liters,
            CostPerLiter = costPerLiter,
            SupplierId = SupplierId,
            ProviderExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)),
            CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)),
            VoucherNumber = $"V-{Guid.NewGuid():N}",
            QrPayload = Guid.NewGuid().ToString(),
            Status = VoucherStatus.Available,
            ImportJobId = importId,
            CreatedAtUtc = DateTime.UtcNow,
        };
}

using FuelFlow.Features.Pricing;
using FluentAssertions;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.BulkActionVouchers;
using FuelFlow.Features.Vouchers.Events;
using FuelFlow.Features.Vouchers.Movements;
using FuelFlow.Features.Vouchers.Exchange;
using FuelFlow.Features.Vouchers.PurchaseBatchCost;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Testcontainers coverage for the operator→brand exchange path (planning #104). Unlike the pure
/// pairing helper (unit-tested on <see cref="VoucherExchangePairing"/>),
/// <see cref="ConfirmVoucherExchangeCommandHandler"/> leans on a real transaction that chains the
/// reuse handlers (SetBatchCost's SaveChanges-first reprice + BulkAction's cost-gate activate) and
/// flips old-voucher statuses, so it only exercises faithfully against real Postgres. The migration
/// seeds fuel types/packages for "okko-95"/"wog-95", so those reprice without extra seeding.
/// </summary>
[Collection("Integration Tests")]
public sealed class VoucherExchangeIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public VoucherExchangeIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HappyPath_EqualCounts_ExpiresOldsActivatesNewsAndWritesPairedRows()
    {
        var actingUserId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var importId = Guid.NewGuid();
        var old1 = Guid.NewGuid();
        var old2 = Guid.NewGuid();
        var new1 = Guid.NewGuid();
        var new2 = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, actingUserId);
            SeedSupplier(seed, supplierId);
            SeedImport(seed, importId);
            // Old stock near/at expiry (operator-owned: no assignment / worker). Both cost 30/L: an
            // exchange carries each voucher's own cost over, it does not take a batch-wide number.
            seed.FuelVouchers.Add(Stock(old1, "okko", "okko-95", 50m, today.AddDays(3), VoucherStatus.Available, supplierId: supplierId, costPerLiter: 30m));
            seed.FuelVouchers.Add(Stock(old2, "okko", "okko-95", 50m, today.AddDays(1), VoucherStatus.Available, supplierId: supplierId, costPerLiter: 30m));
            // Freshly imported replacements (Imported = not yet on sale, awaiting cost + activate).
            seed.FuelVouchers.Add(Stock(new1, "okko", "okko-95", 50m, today.AddMonths(6), VoucherStatus.Imported, importId, supplierId: supplierId));
            seed.FuelVouchers.Add(Stock(new2, "okko", "okko-95", 50m, today.AddMonths(6), VoucherStatus.Imported, importId, supplierId: supplierId));

            await seed.SaveChangesAsync();
        }

        ConfirmVoucherExchangeResult result;
        using (var ctx = CreateContext())
        {
            result = await BuildHandler(ctx).HandleAsync(new ConfirmVoucherExchangeCommand(
                OldVoucherIds: new List<Guid> { old1, old2 },
                NewImportId: importId,
                Costs: new List<ExchangeFuelCost> { new("okko-95", 30m) },
                SurchargeUah: 200m,
                ActingUserId: actingUserId,
                ActingUserName: "Operator"));
        }

        result.Success.Should().BeTrue(result.Error);
        result.ExpiredCount.Should().Be(2);
        result.NewActivatedCount.Should().Be(2);
        result.PairedCount.Should().Be(2);
        result.UnpairedOldCount.Should().Be(0);
        result.UnpairedNewCount.Should().Be(0);
        // planning #136 — the 200 UAH surcharge must raise the repriced cost, not just sit in the audit
        // row: old 30 + 200/100 new L = 32, carried onto each replacement's own cost.
        result.BlendedCostPerLiter.Should().Be(32m);

        using var verify = CreateContext();

        var olds = await verify.FuelVouchers.AsNoTracking().Where(v => v.Id == old1 || v.Id == old2).ToListAsync();
        olds.Should().OnlyContain(v => v.Status == VoucherStatus.Expired);

        var news = await verify.FuelVouchers.AsNoTracking().Where(v => v.Id == new1 || v.Id == new2).ToListAsync();
        news.Should().OnlyContain(v => v.Status == VoucherStatus.Available);
        news.Should().OnlyContain(v => v.CostPerLiter == 32m);   // carried over + this voucher's surcharge share
        news.Should().OnlyContain(v => v.SupplierId == supplierId);

        var exchanges = await verify.VoucherExchanges.AsNoTracking().ToListAsync();
        exchanges.Should().HaveCount(2);
        exchanges.Should().OnlyContain(x => x.ExchangeBatchId == result.ExchangeBatchId);
        exchanges.Should().OnlyContain(x => x.NewVoucherId != null);
        exchanges.Should().OnlyContain(x => x.SupplierId == supplierId);
        // Per row, not a batch copy: each old voucher carried 30 + its own 1.00/L share (200 over 100 L).
        exchanges.Should().OnlyContain(x => x.CostPerLiterApplied == 32m);
        exchanges.Should().OnlyContain(x => x.SurchargeUah == 100m);   // 50 L of the 100 new L
        exchanges.Sum(x => x.SurchargeUah).Should().Be(200m);          // the whole surcharge, once

        // D-13. One movement per piece of paper that changed hands, all under the batch - so an
        // exchange is one settlement with a direction per voucher, rather than per-pair rows that have
        // to be re-joined to say which paper went where.
        var movements = await verify.VoucherMovements.AsNoTracking().ToListAsync();
        movements.Should().HaveCount(4, "two retired out to the supplier, two received back");

        movements.Should().OnlyContain(m => m.BatchId == result.ExchangeBatchId);

        var outMovements = movements.Where(m => m.Kind == VoucherMovementKinds.ExchangeOut).ToList();
        outMovements.Should().HaveCount(2);
        outMovements.Select(m => m.VoucherId).Should().BeEquivalentTo([old1, old2]);
        outMovements.Should().OnlyContain(m =>
            m.FromLocation == VoucherLocations.Pool && m.ToLocation == VoucherLocations.Supplier);

        var inMovements = movements.Where(m => m.Kind == VoucherMovementKinds.ExchangeIn).ToList();
        inMovements.Should().HaveCount(2);
        inMovements.Select(m => m.VoucherId).Should().BeEquivalentTo([new1, new2]);
        inMovements.Should().OnlyContain(m =>
            m.FromLocation == VoucherLocations.Supplier && m.ToLocation == VoucherLocations.Pool);

        // The money is NOT copied per voucher: the settlement total is on the batch, and each voucher's
        // own cost is on the voucher. A third number would be a third thing to reconcile.
        movements.Should().OnlyContain(m => m.AmountUah == null && m.CostDeltaUah == null);

        // Both halves of the swap are now recorded, and the split is the point: a status change is
        // written by the code that PERFORMS it, not by the handler that arranged it.
        //
        // The retirement is performed by this handler's own statement, so it writes it. The activation
        // is delegated to BulkActionVouchersCommandHandler, so that writes it — which is why the event
        // names the operator rather than the exchange, and why neither row is a "the exchange did this"
        // claim. Before, neither was recorded at all.
        var events = await verify.VoucherEvents.AsNoTracking().ToListAsync();
        events.Should().HaveCount(4, "two olds retired by this handler, two news activated by the bulk handler");

        var retired = events.Where(e => e.VoucherId == old1 || e.VoucherId == old2).ToList();
        retired.Should().HaveCount(2);
        retired.Should().OnlyContain(e =>
            e.EventType == VoucherEventTypes.StatusChanged &&
            e.ToStatus == nameof(VoucherStatus.Expired) &&
            e.ActorKind == VoucherActorKinds.User);
        retired.Select(e => e.VoucherId).Should().BeEquivalentTo([old1, old2]);

        var activated = events.Where(e => e.VoucherId == new1 || e.VoucherId == new2).ToList();
        activated.Should().HaveCount(2);
        activated.Should().OnlyContain(e =>
            e.EventType == VoucherEventTypes.StatusChanged &&
            e.FromStatus == nameof(VoucherStatus.Imported) &&
            e.ToStatus == nameof(VoucherStatus.Available));
        activated.Select(e => e.VoucherId).Should().BeEquivalentTo([new1, new2]);

        events.Should().OnlyContain(e => e.ActorUserId == actingUserId,
            "both halves were performed by the operator who confirmed the exchange");

        var audit = await verify.Set<AuditEventOutbox>().AsNoTracking()
            .Where(e => e.EventType == "VoucherExchanged").ToListAsync();
        audit.Should().ContainSingle(e => e.AggregateId == result.ExchangeBatchId.ToString());
    }

    /// <summary>
    /// The scenario the per-voucher cost model exists for: five stock vouchers of one brand, bought at five
    /// different prices from one supplier, handed over together. Each replacement must carry its OWN old
    /// price plus the доплата — a single batch-wide number would misreport four of the five rows during
    /// supplier reconciliation, and would move the blended price by the wrong amount.
    /// </summary>
    [Fact]
    public async Task FiveVouchers_FiveDifferentCosts_EachReplacementCarriesItsOwnCost()
    {
        var actingUserId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var importId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var oldCosts = new[] { 88.00m, 91.50m, 93.00m, 95.25m, 97.00m };
        var oldIds = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        var newIds = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, actingUserId);
            SeedSupplier(seed, supplierId);
            SeedImport(seed, importId);

            for (var i = 0; i < 5; i++)
            {
                // Different expiry dates too: they aged differently before being handed over.
                seed.FuelVouchers.Add(Stock(oldIds[i], "okko", "okko-95", 10m,
                    today.AddDays(1 + i), VoucherStatus.Available,
                    supplierId: supplierId, costPerLiter: oldCosts[i]));
                seed.FuelVouchers.Add(Stock(newIds[i], "okko", "okko-95", 10m,
                    today.AddMonths(6), VoucherStatus.Imported, importId,
                    supplierId: supplierId));
            }

            await seed.SaveChangesAsync();
        }

        // 2.00 UAH/L доплата on 50 new liters = 100.00 UAH.
        const decimal surchargePerLiter = 2m;
        var surcharge = 5m * 10m * surchargePerLiter;

        ConfirmVoucherExchangeResult result;
        using (var ctx = CreateContext())
        {
            result = await BuildHandler(ctx).HandleAsync(new ConfirmVoucherExchangeCommand(
                OldVoucherIds: oldIds,
                NewImportId: importId,
                Costs: new List<ExchangeFuelCost> { new("okko-95", 90m) },
                SurchargeUah: surcharge,
                ActingUserId: actingUserId,
                ActingUserName: "Operator"));
        }

        result.Success.Should().BeTrue(result.Error);
        result.PairedCount.Should().Be(5);
        result.UnpairedNewCount.Should().Be(0);

        using var verify = CreateContext();

        var exchanges = await verify.VoucherExchanges.AsNoTracking().ToListAsync();
        exchanges.Should().HaveCount(5);

        // Each row's cost is its own old price + 2.00, matched through the pairing, not a copy.
        var pairs = exchanges.ToDictionary(x => x.OldVoucherId, x => x.NewVoucherId!.Value);
        var oldCostById = oldIds.Zip(oldCosts).ToDictionary(p => p.First, p => p.Second);

        foreach (var (oldId, newId) in pairs)
        {
            exchanges.Single(x => x.OldVoucherId == oldId).CostPerLiterApplied
                .Should().Be(oldCostById[oldId] + surchargePerLiter);
            exchanges.Single(x => x.OldVoucherId == oldId).SurchargeUah
                .Should().Be(10m * surchargePerLiter);   // this voucher's share of the 100.00
        }

        var news = await verify.FuelVouchers.AsNoTracking()
            .Where(v => newIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id);
        foreach (var (oldId, newId) in pairs)
        {
            news[newId].CostPerLiter.Should().Be(oldCostById[oldId] + surchargePerLiter);
        }

        // Conservation: value out plus the доплата equals value in.
        var valueOut = oldCosts.Sum(c => c * 10m);
        var valueIn = oldCosts.Sum(c => (c + surchargePerLiter) * 10m);
        valueIn.Should().Be(valueOut + surcharge);

        // The blend is the liters-weighted mean of five different prices, not one of them.
        var expectedBlended = oldCosts.Average();
        result.BlendedCostPerLiter.Should().Be(expectedBlended + surchargePerLiter);
    }

    /// <summary>
    /// A sale does not move the price. Settled with the operator on 2026-10-17 and recorded in CODEBASE.md
    /// (`purchase_batches`) and planning #360: the moving average is recomputed when a lot arrives and
    /// at no other time.
    /// <para>
    /// This used to call <c>RepriceAsync</c>, so every exchange moved the quoted price by however much
    /// stock it happened to consume — and in which direction depended on which vouchers the sale took.
    /// Both halves are asserted here: the offer's own cost column, and the absence of a price-history
    /// row with the <c>exchange-release</c> reason. The second is the sharper one — it says no price
    /// event happened, rather than that one happened and happened to land on the same number.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AnExchange_MovesThePriceOfNothing()
    {
        var actingUserId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var importId = Guid.NewGuid();
        var oldIds = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();
        var newIds = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        decimal costBefore;
        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, actingUserId);
            SeedSupplier(seed, supplierId);
            SeedImport(seed, importId);

            for (var i = 0; i < 3; i++)
            {
                seed.FuelVouchers.Add(Stock(oldIds[i], "okko", "okko-95", 10m,
                    today.AddDays(1 + i), VoucherStatus.Available,
                    supplierId: supplierId, costPerLiter: 60m));
                seed.FuelVouchers.Add(Stock(newIds[i], "okko", "okko-95", 10m,
                    today.AddMonths(6), VoucherStatus.Imported, importId,
                    supplierId: supplierId));
            }

            await seed.SaveChangesAsync();

            costBefore = (await seed.FuelOffers.AsNoTracking()
                .Where(o => o.FuelTypeId == "okko-95")
                .Select(o => o.SupplierPricePerLiter)
                .FirstAsync())!.Value;
        }

        using (var ctx = CreateContext())
        {
            var result = await BuildHandler(ctx).HandleAsync(new ConfirmVoucherExchangeCommand(
                OldVoucherIds: oldIds,
                NewImportId: importId,
                Costs: new List<ExchangeFuelCost> { new("okko-95", 60m) },
                SurchargeUah: 300m,
                ActingUserId: actingUserId,
                ActingUserName: "Operator"));
            result.Success.Should().BeTrue(result.Error);
        }

        using var verify = CreateContext();

        (await verify.FuelOffers.AsNoTracking()
            .Where(o => o.FuelTypeId == "okko-95")
            .Select(o => o.SupplierPricePerLiter)
            .FirstAsync()).Should().Be(
            costBefore,
            "three vouchers left the stock, but a sale is not a purchase and moves nothing");

        (await verify.FuelOfferPrices.AsNoTracking()
            .Where(p => p.FuelTypeId == "okko-95"
                && p.Reason == SharedKernel.Domain.OfferPriceReasons.ExchangeRelease)
            .CountAsync()).Should().Be(
            0,
            "and no price event was recorded for it either - the rule is that the price did not move");
    }

    [Fact]
    public async Task Exchange_MixingTwoSuppliers_IsRefusedAndNamesBoth()
    {
        var actingUserId = Guid.NewGuid();
        var supplierA = Guid.NewGuid();
        var supplierB = Guid.NewGuid();
        var importId = Guid.NewGuid();
        var oldA = Guid.NewGuid();
        var oldB = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, actingUserId);
            SeedSupplier(seed, supplierA, "Supplier A");
            SeedSupplier(seed, supplierB, "Supplier B");
            SeedImport(seed, importId);

            seed.FuelVouchers.Add(Stock(oldA, "okko", "okko-95", 10m, today.AddDays(1), VoucherStatus.Available, supplierId: supplierA, costPerLiter: 90m));
            seed.FuelVouchers.Add(Stock(oldB, "okko", "okko-95", 10m, today.AddDays(1), VoucherStatus.Available, supplierId: supplierB, costPerLiter: 90m));
            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            var result = await BuildHandler(ctx).HandleAsync(new ConfirmVoucherExchangeCommand(
                OldVoucherIds: new List<Guid> { oldA, oldB },
                NewImportId: importId,
                Costs: new List<ExchangeFuelCost> { new("okko-95", 90m) },
                SurchargeUah: 0m,
                ActingUserId: actingUserId,
                ActingUserName: "Operator"));

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("one supplier");
            result.Error.Should().Contain("Supplier A");
            result.Error.Should().Contain("Supplier B");
        }

        using var verify = CreateContext();
        (await verify.VoucherExchanges.CountAsync()).Should().Be(0);
        var olds = await verify.FuelVouchers.AsNoTracking().Where(v => v.Id == oldA || v.Id == oldB).ToListAsync();
        olds.Should().OnlyContain(v => v.Status == VoucherStatus.Available);   // nothing retired
    }

    [Fact]
    public async Task Exchange_RefusesAVoucherWithNoCost_BecauseValueCannotBeCarriedOver()
    {
        var actingUserId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var importId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var uncosted = Guid.NewGuid();

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, actingUserId);
            SeedSupplier(seed, supplierId);
            SeedImport(seed, importId);
            seed.FuelVouchers.Add(Stock(uncosted, "okko", "okko-95", 10m, today.AddDays(1), VoucherStatus.Available, supplierId: supplierId));
            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            var result = await BuildHandler(ctx).HandleAsync(new ConfirmVoucherExchangeCommand(
                OldVoucherIds: new List<Guid> { uncosted },
                NewImportId: importId,
                Costs: new List<ExchangeFuelCost> { new("okko-95", 90m) },
                SurchargeUah: 0m,
                ActingUserId: actingUserId,
                ActingUserName: "Operator"));

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("no cost recorded");
        }
    }

    [Fact]
    public async Task ZeroSurcharge_RepricesAtBaseCostWithNoInflation()
    {
        // planning #136 guard: with no surcharge the fold is a no-op — the repriced and applied cost
        // must stay exactly the operator-entered base (we must not accidentally inflate at zero).
        var actingUserId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var importId = Guid.NewGuid();
        var old1 = Guid.NewGuid();
        var new1 = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, actingUserId);
            SeedSupplier(seed, supplierId);
            SeedImport(seed, importId);
            seed.FuelVouchers.Add(Stock(old1, "okko", "okko-95", 50m, today.AddDays(1), VoucherStatus.Available, supplierId: supplierId, costPerLiter: 30m));
            seed.FuelVouchers.Add(Stock(new1, "okko", "okko-95", 50m, today.AddMonths(6), VoucherStatus.Imported, importId, supplierId: supplierId));

            await seed.SaveChangesAsync();
        }

        ConfirmVoucherExchangeResult result;
        using (var ctx = CreateContext())
        {
            result = await BuildHandler(ctx).HandleAsync(new ConfirmVoucherExchangeCommand(
                new List<Guid> { old1 }, importId,
                new List<ExchangeFuelCost> { new("okko-95", 30m) },
                SurchargeUah: 0m, actingUserId, "Operator"));
        }

        result.Success.Should().BeTrue(result.Error);
        result.BlendedCostPerLiter.Should().Be(30m); // no surcharge → cost unchanged

        using var verify = CreateContext();
        var exchanges = await verify.VoucherExchanges.AsNoTracking().ToListAsync();
        exchanges.Should().OnlyContain(x => x.SurchargeUah == 0m && x.CostPerLiterApplied == 30m);
        var replacement = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == new1);
        replacement.CostPerLiter.Should().Be(30m);
    }

    [Fact]
public async Task MoreOldThanNew_IsRefused_BecausePaperWouldBeLost()
    {
        // Retiring an old voucher with nothing to replace it would hand paper to the brand and record no
        // replacement, so the exchange is refused rather than allowed to expire an old voucher with a null
        // new voucher — that record said "renewed" while nothing came back.
        var actingUserId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var importId = Guid.NewGuid();
        var old1 = Guid.NewGuid();
        var old2 = Guid.NewGuid();
        var new1 = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, actingUserId);
            SeedSupplier(seed, supplierId);
            SeedImport(seed, importId);
            seed.FuelVouchers.Add(Stock(old1, "okko", "okko-95", 50m, today.AddDays(-2), VoucherStatus.Expired, supplierId: supplierId, costPerLiter: 30m));
            seed.FuelVouchers.Add(Stock(old2, "okko", "okko-95", 50m, today.AddDays(2), VoucherStatus.Available, supplierId: supplierId, costPerLiter: 30m));
            seed.FuelVouchers.Add(Stock(new1, "okko", "okko-95", 50m, today.AddMonths(6), VoucherStatus.Imported, importId, supplierId: supplierId));

            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            var result = await BuildHandler(ctx).HandleAsync(new ConfirmVoucherExchangeCommand(
                new List<Guid> { old1, old2 }, importId,
                new List<ExchangeFuelCost> { new("okko-95", 28m) },
                150m, actingUserId, "Operator"));

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("one-for-one");
        }

        using var verify = CreateContext();
        (await verify.VoucherExchanges.CountAsync()).Should().Be(0);

        var olds = await verify.FuelVouchers.AsNoTracking().Where(v => v.Id == old1 || v.Id == old2).ToListAsync();
        olds.Should().OnlyContain(v => v.Status != VoucherStatus.Expired || v.Id == old1);  // the already-lapsed one stays lapsed
        (await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == old2)).Status
            .Should().Be(VoucherStatus.Available);   // the sellable one was NOT retired
    }

    [Fact]
public async Task MoreNewThanOld_IsRefused_SoSurplusCannotEnterStockCheaply()
    {
        // A PDF holding more vouchers than were handed over is a normal purchase that happens to include
        // the replacements, not an exchange. Allowing it would put the surplus on sale at the invoice price
        // instead of the value it replaced, silently dragging the blended cost down.
        var actingUserId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var importId = Guid.NewGuid();
        var old1 = Guid.NewGuid();
        var new1 = Guid.NewGuid();
        var new2 = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, actingUserId);
            SeedSupplier(seed, supplierId);
            SeedImport(seed, importId);
            seed.FuelVouchers.Add(Stock(old1, "okko", "okko-95", 50m, today.AddDays(1), VoucherStatus.Available, supplierId: supplierId, costPerLiter: 30m));
            seed.FuelVouchers.Add(Stock(new1, "okko", "okko-95", 50m, today.AddMonths(6), VoucherStatus.Imported, importId, supplierId: supplierId));
            seed.FuelVouchers.Add(Stock(new2, "okko", "okko-95", 50m, today.AddMonths(6), VoucherStatus.Imported, importId, supplierId: supplierId));

            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            var result = await BuildHandler(ctx).HandleAsync(new ConfirmVoucherExchangeCommand(
                new List<Guid> { old1 }, importId,
                new List<ExchangeFuelCost> { new("okko-95", 26m) },
                100m, actingUserId, "Operator"));

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("one-for-one");
            result.Error.Should().Contain("Import the rest as a normal purchase");
        }

        using var verify = CreateContext();

        // Nothing moved: no retirement, no activation, no audit row.
        (await verify.VoucherExchanges.CountAsync()).Should().Be(0);

        var old = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == old1);
        old.Status.Should().Be(VoucherStatus.Available);

        var news = await verify.FuelVouchers.AsNoTracking().Where(v => v.Id == new1 || v.Id == new2).ToListAsync();
        news.Should().OnlyContain(v => v.Status == VoucherStatus.Imported);
        news.Should().OnlyContain(v => v.CostPerLiter == null);   // not priced from the invoice either
    }

    [Fact]
    public async Task Validation_AssignedOldVoucher_IsRejectedAndNothingChanges()
    {
        var actingUserId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var importId = Guid.NewGuid();
        var assignedOld = Guid.NewGuid();
        var new1 = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, actingUserId);
            SeedUser(seed, customerId);
            SeedImport(seed, importId);
            // The customer's voucher must look legitimately held: an order delivered it, so
            // ck_voucher_held_has_order is satisfied and the rejection below is provably about the
            // exchange rules, not about an impossible row.
            var purchaseOrderId = SeedPurchaseOrder(seed, customerId);
            var assigned = Stock(assignedOld, "okko", "okko-95", 50m, today.AddDays(1), VoucherStatus.Assigned, orderId: purchaseOrderId);
            assigned.AssignedToUserId = customerId; // a customer's voucher — out of scope for v1
            seed.FuelVouchers.Add(assigned);
            seed.FuelVouchers.Add(Stock(new1, "okko", "okko-95", 50m, today.AddMonths(6), VoucherStatus.Imported, importId));

            await seed.SaveChangesAsync();
        }

        ConfirmVoucherExchangeResult result;
        using (var ctx = CreateContext())
        {
            result = await BuildHandler(ctx).HandleAsync(new ConfirmVoucherExchangeCommand(
                new List<Guid> { assignedOld }, importId,
                new List<ExchangeFuelCost> { new("okko-95", 30m) },
                200m, actingUserId, "Operator"));
        }

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();

        using var verify = CreateContext();
        var old = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == assignedOld);
        old.Status.Should().Be(VoucherStatus.Assigned); // untouched
        var newV = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == new1);
        newV.Status.Should().Be(VoucherStatus.Imported); // never activated
        (await verify.VoucherExchanges.AsNoTracking().AnyAsync()).Should().BeFalse();
        (await verify.PurchaseBatches.AsNoTracking().AnyAsync(b => b.ImportJobId == importId)).Should().BeFalse();

        // D-13. A refused exchange must leave no custody history either. The movements are written in the
        // same transaction as the batch and the pair rows, and `tx.CommitAsync` runs only on the success
        // path - so a rejection rolls them back. This is the gate that makes the exchange writers safe,
        // and it is worth asserting rather than trusting: a movement for a voucher that never went to the
        // supplier is exactly the kind of false history the ledger exists to prevent.
        (await verify.VoucherMovements.AsNoTracking().AnyAsync())
            .Should().BeFalse("a refused exchange must not record that any paper changed hands");
        (await verify.VoucherEvents.AsNoTracking().AnyAsync())
            .Should().BeFalse("nor that anything changed state");
    }

    [Fact]
    public async Task Validation_UnknownImport_IsRejected()
    {
var actingUserId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var old1 = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, actingUserId);
            SeedSupplier(seed, supplierId);
            seed.FuelVouchers.Add(Stock(old1, "okko", "okko-95", 50m, today.AddDays(1), VoucherStatus.Available, supplierId: supplierId, costPerLiter: 30m));

            await seed.SaveChangesAsync();
        }

        using var ctx = CreateContext();
        var result = await BuildHandler(ctx).HandleAsync(new ConfirmVoucherExchangeCommand(
            new List<Guid> { old1 }, Guid.NewGuid(), // import that has no vouchers
            new List<ExchangeFuelCost> { new("okko-95", 30m) },
            200m, actingUserId, "Operator"));

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Idempotency_ReExchangingAnAlreadyExchangedVoucher_IsRejected()
    {
        var actingUserId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var importId = Guid.NewGuid();
        var secondImportId = Guid.NewGuid();
        var old1 = Guid.NewGuid();
        var new1 = Guid.NewGuid();
        var new2 = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, actingUserId);
            SeedSupplier(seed, supplierId);
            SeedImport(seed, importId);
            SeedImport(seed, secondImportId);
            seed.FuelVouchers.Add(Stock(old1, "okko", "okko-95", 50m, today.AddDays(1), VoucherStatus.Available, supplierId: supplierId, costPerLiter: 30m));
            seed.FuelVouchers.Add(Stock(new1, "okko", "okko-95", 50m, today.AddMonths(6), VoucherStatus.Imported, importId, supplierId: supplierId));
            seed.FuelVouchers.Add(Stock(new2, "okko", "okko-95", 50m, today.AddMonths(6), VoucherStatus.Imported, secondImportId, supplierId: supplierId));

            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            var first = await BuildHandler(ctx).HandleAsync(new ConfirmVoucherExchangeCommand(
                new List<Guid> { old1 }, importId,
                new List<ExchangeFuelCost> { new("okko-95", 30m) },
                200m, actingUserId, "Operator"));
            first.Success.Should().BeTrue(first.Error);
        }

        ConfirmVoucherExchangeResult second;
        using (var ctx = CreateContext())
        {
            // old1 is already recorded in voucher_exchanges → re-confirm must be refused (no double-apply).
            second = await BuildHandler(ctx).HandleAsync(new ConfirmVoucherExchangeCommand(
                new List<Guid> { old1 }, secondImportId,
                new List<ExchangeFuelCost> { new("okko-95", 32m) },
                200m, actingUserId, "Operator"));
        }

        second.Success.Should().BeFalse();

        using var verify = CreateContext();
        (await verify.VoucherExchanges.AsNoTracking().CountAsync()).Should().Be(1); // only the first stuck
        var leftoverNew = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == new2);
        leftoverNew.Status.Should().Be(VoucherStatus.Imported); // second batch never activated
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private static ConfirmVoucherExchangeCommandHandler BuildHandler(ApplicationDbContext ctx)
    {
        var recalculator = new BlendedCostRecalculator(ctx, new OfferPriceRecorder(ctx));
        var eventService = new AuditEventService(ctx);

        var backgroundJobClient = new Mock<IBackgroundJobClient>();
        backgroundJobClient.Setup(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>())).Returns("job-id");
        var bulkAction = new BulkActionVouchersCommandHandler(ctx, backgroundJobClient.Object, eventService);

        return new ConfirmVoucherExchangeCommandHandler(ctx, recalculator, bulkAction, eventService);
    }

    private static void SeedUser(ApplicationDbContext ctx, Guid userId)
        => ctx.Users.Add(new User
        {
            Id = userId,
            PhoneNumber = $"+38{userId:N}"[..20],
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

    private static void SeedSupplier(ApplicationDbContext ctx, Guid supplierId, string name = "Seed Supplier")
        => ctx.Suppliers.Add(new Supplier
        {
            Id = supplierId,
            Name = name,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

    private static void SeedImport(ApplicationDbContext ctx, Guid importId)
        => ctx.VoucherImports.Add(new VoucherImport
        {
            Id = importId,
            FileName = $"import-{importId:N}.pdf",
            Status = "Completed",
            StartedAtUtc = DateTime.UtcNow,
            CompletedAtUtc = DateTime.UtcNow
        });

    /// <summary>
    /// The purchase that delivered a customer's voucher. A held voucher (Assigned/Used/Blocked) must
    /// carry an <c>order_id</c> — ck_voucher_held_has_order rejects the row otherwise. Warehouse stock
    /// (Available/Imported/Expired) must keep it null, so <see cref="Stock"/> takes it as optional.
    /// </summary>
    private static Guid SeedPurchaseOrder(ApplicationDbContext ctx, Guid userId)
    {
        var orderId = Guid.NewGuid();
        ctx.Orders.Add(new Order
        {
            Id = orderId,
            UserId = userId,
            Price = 0,
            Status = OrderStatus.Fulfilled,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        return orderId;
    }

    private static FuelVoucher Stock(Guid id, string brand, string fuelTypeId, decimal liters, DateOnly expiry, VoucherStatus status, Guid? importId = null, Guid? orderId = null, Guid? supplierId = null, decimal? costPerLiter = null)
        => new()
        {
            Id = id,
            Brand = brand,
            FuelTypeId = fuelTypeId,
            Liters = liters,
            SupplierExpirationDate = expiry,
            CustomerExpirationDate = expiry,
            VoucherNumber = $"VX-{id:N}"[..16],
            QrPayload = $"qr-{id:N}",
            Status = status,
            ImportJobId = importId,
            OrderId = orderId,
            SupplierId = supplierId,
            CostPerLiter = costPerLiter,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    private static async Task ResetDataAsync(ApplicationDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "voucher_exchanges", "movement_batches", "purchase_batches", "voucher_imports", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "suppliers", "users", "provider_event_outbox" RESTART IDENTITY CASCADE""");
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);
}

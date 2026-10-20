using FluentAssertions;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Exchange;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The exchange audit read as batches and as pairs (#223), against a real PostgreSQL database.
/// </summary>
/// <remarks>
/// This surface exists because <c>voucher_exchanges</c> was written by every exchange and read by
/// nothing. The thing worth protecting is the arithmetic: each row carries only its OWN share of the
/// batch surcharge, so the batch's charge is the sum of the rows, and the vouchers of one exchange
/// deliberately carry different costs per liter so nothing can be reported as a single flat number.
///
/// The same mistake shipped once already — the per-batch cost timeline read one row instead of
/// summing and displayed 2.00 UAH for a 100 UAH exchange. These tests are written so that reading a
/// single row would fail them.
/// </remarks>
[Collection("Integration Tests")]
public sealed class VoucherExchangeHistoryIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private static readonly Guid SupplierA = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SupplierB = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly TestDatabaseFixture _fixture;

    public VoucherExchangeHistoryIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Batches_ReportTheSurchargeAsTheSumOfThePairsShares()
    {
        var batch = Guid.NewGuid();
        await SeedExchangeAsync(batch, SupplierA, "Alpha", new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc),
            new decimal?[] { 88.00m, 91.50m, 93.00m, 95.25m }, surchargePerPair: 25m);

        var response = await BatchesAsync();

        var row = response.Data.Should().ContainSingle().Subject;
        row.ExchangeBatchId.Should().Be(batch);
        // 4 x 25. Reading one row's share would report 25 for a 100 UAH exchange.
        row.SurchargeUah.Should().Be(100m);
        row.VouchersExchanged.Should().Be(4);
        row.SupplierName.Should().Be("Alpha");
        row.RecordedBy.Should().Be("Operator One");
    }

    [Fact]
    public async Task Batches_WeighTheRetiredVouchersCostByTheirLitres()
    {
        // 40 L at 88.00 and 10 L at 96.00: the 40 L voucher counts for four of the 10 L one.
        var batch = Guid.NewGuid();
        await SeedExchangeAsync(batch, SupplierA, "Alpha", new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc),
            new decimal?[] { 88.00m, 96.00m }, surchargePerPair: 10m, litresPerVoucher: new[] { 40m, 10m });

        var row = (await BatchesAsync()).Data.Should().ContainSingle().Subject;

        // (40*88 + 10*96) / 50 = 89.60. The plain mean would be 92.00 and a first-row read 88.00.
        row.CostPerLiterBefore.Should().Be(89.60m);
        row.LitersOut.Should().Be(50m);
    }

    [Fact]
    public async Task Batches_ReportNoBeforeCostWhenARetiredVoucherHasNone()
    {
        var batch = Guid.NewGuid();
        await SeedExchangeAsync(batch, SupplierA, "Alpha", new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc),
            new decimal?[] { 88.00m, null }, surchargePerPair: 10m);

        var row = (await BatchesAsync()).Data.Should().ContainSingle().Subject;

        // A mean over the pairs we do know would read as a firmer number than the record supports.
        row.CostPerLiterBefore.Should().BeNull();
    }

    [Fact]
    public async Task Batches_AreOneRowPerConfirmNotOneRowPerVoucher()
    {
        var batch = Guid.NewGuid();
        await SeedExchangeAsync(batch, SupplierA, "Alpha", new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc),
            new decimal?[] { 88.00m, 91.50m, 93.00m }, surchargePerPair: 20m);

        var response = await BatchesAsync();

        response.Data.Should().ContainSingle();
        response.Total.Should().Be(1);
        response.Data[0].VouchersExchanged.Should().Be(3);
    }

    [Fact]
    public async Task Batches_AreNewestFirstAndPageWithoutRepeating()
    {
        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();
        await SeedExchangeAsync(older, SupplierA, "Alpha", new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc),
            new decimal?[] { 88.00m }, surchargePerPair: 10m);
        await SeedExchangeAsync(newer, SupplierA, "Alpha", new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc),
new decimal?[] { 90.00m }, surchargePerPair: 10m, reset: false);

        var firstPage = await BatchesAsync(page: 1, limit: 1);

        firstPage.Data.Should().ContainSingle().Which.ExchangeBatchId.Should().Be(newer);
        firstPage.Total.Should().Be(2);

        var secondPage = await BatchesAsync(page: 2, limit: 1);
        secondPage.Data.Should().ContainSingle().Which.ExchangeBatchId.Should().Be(older);
    }

    [Fact]
    public async Task Batches_FilterBySupplier()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await SeedExchangeAsync(a, SupplierA, "Alpha", new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc),
            new decimal?[] { 88.00m }, surchargePerPair: 10m);
        await SeedExchangeAsync(b, SupplierB, "Beta", new DateTime(2026, 10, 2, 10, 0, 0, DateTimeKind.Utc),
new decimal?[] { 90.00m }, surchargePerPair: 10m, reset: false);

        var filtered = await BatchesAsync(supplierId: SupplierB);

        var row = filtered.Data.Should().ContainSingle().Subject;
        row.ExchangeBatchId.Should().Be(b);
        row.SupplierName.Should().Be("Beta");
        // The dropdown offers both, so the operator can see the filter has something to select.
        filtered.Suppliers.Select(s => s.Name).Should().BeEquivalentTo(["Alpha", "Beta"]);
    }

    [Fact]
    public async Task Batches_FilterByDateRangeIncludingTheEndDay()
    {
        var early = Guid.NewGuid();
        var late = Guid.NewGuid();
        await SeedExchangeAsync(early, SupplierA, "Alpha", new DateTime(2026, 10, 1, 23, 0, 0, DateTimeKind.Utc),
            new decimal?[] { 88.00m }, surchargePerPair: 10m);
        await SeedExchangeAsync(late, SupplierA, "Alpha", new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc),
new decimal?[] { 90.00m }, surchargePerPair: 10m, reset: false);

        var inRange = await BatchesAsync(from: new DateOnly(2026, 10, 1), to: new DateOnly(2026, 10, 1));

        // The early batch is at 23:00 on the 1st; an exclusive end would silently drop it.
        inRange.Data.Should().ContainSingle().Which.ExchangeBatchId.Should().Be(early);
    }

    [Fact]
    public async Task Batches_SurviveARetiredVoucherBeingSoftDeleted()
    {
        var batch = Guid.NewGuid();
        await SeedExchangeAsync(batch, SupplierA, "Alpha", new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc),
            new decimal?[] { 88.00m, 91.50m }, surchargePerPair: 25m, softDeleteReplacements: true);

        var row = (await BatchesAsync()).Data.Should().ContainSingle().Subject;

        // The replacements are soft-deleted. An audit row about an exchange must outlive the stock.
        row.VouchersExchanged.Should().Be(2);
        row.LitersOut.Should().Be(20m);
    }

    [Fact]
    public async Task Batch_ShowsEveryPairWithItsOwnCostAndShare()
    {
        var batch = Guid.NewGuid();
        await SeedExchangeAsync(batch, SupplierA, "Alpha", new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc),
            new decimal?[] { 88.00m, 91.50m, 93.00m, 95.25m }, surchargePerPair: 25m);

        var detail = await BatchAsync(batch);

        detail.Should().NotBeNull();
        detail!.SupplierName.Should().Be("Alpha");
        detail.RecordedBy.Should().Be("Operator One");
        detail.Pairs.Should().HaveCount(4);

        // Each row carries the cost of ITS OWN voucher - the reason the exchange exists at all.
        detail.Pairs.Select(p => p.OldCostPerLiter).Should().BeEquivalentTo([88.00m, 91.50m, 93.00m, 95.25m]);
        detail.Pairs.Should().OnlyContain(p => p.SurchargeShareUah == 25m);
        detail.Pairs.Should().OnlyContain(p => p.OldVoucherNumber != null && p.NewVoucherNumber != null);

        // The total agrees with the list, and is the sum - not one row's share.
        detail.TotalSurchargeUah.Should().Be(100m);
        detail.Pairs.Sum(p => p.SurchargeShareUah).Should().Be(detail.TotalSurchargeUah);
    }

    [Fact]
    public async Task Batch_TotalsMatchWhatTheListReportsForTheSameBatch()
    {
        var batch = Guid.NewGuid();
        await SeedExchangeAsync(batch, SupplierA, "Alpha", new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc),
            new decimal?[] { 88.00m, 91.50m, 93.00m }, surchargePerPair: 30m);

        var listed = (await BatchesAsync()).Data.Should().ContainSingle().Subject;
        var detail = await BatchAsync(batch);

        detail!.TotalSurchargeUah.Should().Be(listed.SurchargeUah);
        detail.TotalLiters.Should().Be(listed.LitersOut);
        detail.Pairs.Count.Should().Be(listed.VouchersExchanged);
    }

    [Fact]
    public async Task Batch_IsNullForAnIdThatWasNeverExchanged()
    {
        (await BatchAsync(Guid.NewGuid())).Should().BeNull();
        (await BatchAsync(Guid.Empty)).Should().BeNull();
    }

[Fact]
    public async Task Batches_AreEmptyRatherThanBrokenOnAnEmptyAudit()
    {
        await using (var clean = CreateContext())
        {
            await clean.Database.ExecuteSqlRawAsync(
                """TRUNCATE TABLE "payments", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "voucher_exchanges", "movement_batches", "purchase_batches", "voucher_imports", "company_invitations", "company_members", "legal_entities", "verification_codes", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");
        }

        var response = await BatchesAsync();

        response.Data.Should().BeEmpty();
        response.Total.Should().Be(0);
    }

    private Task<VoucherExchangeBatchesResponse> BatchesAsync(
        int page = 1,
        int limit = 20,
        Guid? supplierId = null,
        DateOnly? from = null,
        DateOnly? to = null)
        => new GetVoucherExchangeBatchesQueryHandler(CreateContext())
            .HandleAsync(new GetVoucherExchangeBatchesQuery(page, limit, supplierId, from, to));

    private async Task<VoucherExchangeBatchDetailDto?> BatchAsync(Guid batchId)
        => await new GetVoucherExchangeBatchQueryHandler(CreateContext())
            .HandleAsync(new GetVoucherExchangeBatchQuery(batchId));

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    /// <summary>
    /// Seeds one confirm's worth of audit: a batch, its retired vouchers and their replacements, and
    /// one row per pair carrying that pair's share of the surcharge - the way the confirm handler
    /// writes it, not the way it used to.
    /// </summary>
private async Task SeedExchangeAsync(
        Guid batchId,
        Guid supplierId,
        string supplierName,
        DateTime atUtc,
        decimal?[] costsPerLiter,
        decimal surchargePerPair,
        decimal[]? litresPerVoucher = null,
        bool softDeleteReplacements = false,
        bool reset = true)
    {
        await using var seed = CreateContext();

        // Only the first seed of a test clears the container: this suite shares one Postgres, and a
        // second truncate would erase the first batch and make an ordering assertion pass on one row.
        if (reset)
        {
            await seed.Database.ExecuteSqlRawAsync(
                """TRUNCATE TABLE "payments", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "voucher_exchanges", "movement_batches", "purchase_batches", "voucher_imports", "company_invitations", "company_members", "legal_entities", "verification_codes", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");
        }

        if (!await seed.Suppliers.AnyAsync(s => s.Id == supplierId))
        {
            seed.Suppliers.Add(new Supplier
            {
                EdrIpn = TestEdrIpns.Next(),
                Id = supplierId,
                Name = supplierName,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }

        if (!await seed.FuelTypes.AnyAsync(f => f.Id == ToUuid("okko-dp")))
        {
            seed.FuelTypes.Add(new FuelTypeEntity
            {
                Id = ToUuid("okko-dp"),
                Name = "ДП ЄВРО",
                BrandId = BrandIds.Okko,
                BasePrice = 60m,
                DiscountPrice = 60m,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }

        var importId = Guid.NewGuid();
        seed.VoucherImports.Add(new VoucherImport
        {
            Id = importId,
            FileName = "history.pdf",
            CompletedAtUtc = DateTime.UtcNow,
            Status = "Completed",
            StartedAtUtc = atUtc
        });

// The settlement the pairs belong to (#319). One batch row per SeedExchangeAsync call, carrying
        // what the brand charged for the whole thing: surchargePerPair times the pairs, which is what
        // the pairs add up to as long as every voucher here is paired.
        MovementBatchSeeder.AddForExchange(
            seed, batchId, supplierId, surchargePerPair * costsPerLiter.Count(), atUtc, "Operator One");

foreach (var (cost, index) in costsPerLiter.Select((c, i) => (c, i)))
        {
            var liters = litresPerVoucher is null ? 10m : litresPerVoucher[index];
            // Numbers are unique per batch: voucher_number carries a unique index, and a test that
            // seeds two batches without truncating in between would collide on the second.
            var tag = batchId.ToString("N")[..6];
            var oldVoucher = Voucher(importId, liters, cost, supplierId, $"OLD-{tag}-{index:00}");
            var newVoucher = Voucher(importId, liters, cost + 2.50m, supplierId, $"NEW-{tag}-{index:00}");

            seed.FuelVouchers.Add(oldVoucher);
            seed.FuelVouchers.Add(newVoucher);

            seed.VoucherExchanges.Add(new VoucherExchange
            {
                Id = Guid.NewGuid(),
                ExchangeBatchId = batchId,
                OldVoucherId = oldVoucher.Id,
                NewVoucherId = newVoucher.Id,
                FuelTypeId = ToUuid("okko-dp"),
                Brand = "OKKO",
                SupplierId = supplierId,
                SurchargeUah = surchargePerPair,
                CostPerLiterApplied = cost + 2.50m,
                ActingUserId = Guid.NewGuid(),
                ActingUserName = "Operator One",
                CreatedAtUtc = atUtc
            });
        }

        if (softDeleteReplacements)
        {
            await seed.SaveChangesAsync();
            await seed.FuelVouchers
                .Where(v => v.VoucherNumber!.StartsWith("NEW-"))
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.IsDeleted, true));
            return;
        }

        await seed.SaveChangesAsync();
    }

    private static FuelVoucher Voucher(Guid importId, decimal liters, decimal? costPerLiter, Guid supplierId, string number)
        => new()
        {
            Id = Guid.NewGuid(),
            Brand = "OKKO",
            FuelTypeId = ToUuid("okko-dp"),
            Liters = liters,
            CostPerLiter = costPerLiter,
            SupplierId = supplierId,
            SupplierExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)),
            CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)),
            VoucherNumber = number,
            QrPayload = Guid.NewGuid().ToString(),
            Status = VoucherStatus.Available,
            ImportJobId = importId,
            CreatedAtUtc = DateTime.UtcNow
        };
}

using FluentAssertions;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.MarkVoucherAsUsed;
using FuelFlow.Features.Vouchers.Movements;
using FuelFlow.Features.Vouchers.Redemptions;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Redemption is where the sale is actually recognised: the advance stops being an obligation and
/// becomes revenue, and the stock leaves the warehouse. These tests assert both legs against the real
/// seeded chart, not a purpose-built one - a chart invented for a test would prove the engine works
/// and nothing about the accounts.
/// </summary>
[Collection("Integration Tests")]
public sealed class VoucherRedemptionJournalTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public VoucherRedemptionJournalTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    private async Task ResetAsync(ApplicationDbContext context)
        => await context.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE
                "journal_lines", "journal_entries", "document_sequences", "accounting_periods",
                "voucher_redemptions", "redemption_statement_rows",
                "voucher_events", "voucher_movements", "order_line_items", "orders",
                "fuel_vouchers", "users"
            CASCADE;
            """);

    /// <summary>A voucher sold at 2500 gross with 50 liters bought at 45.00 - a 250 margin.</summary>
    private async Task<(Guid UserId, Guid VoucherId)> SeedSaleAsync(ApplicationDbContext context, decimal costPerLiter)
    {
        var (userId, voucherIds) = await SeedPackSaleAsync(
            context, quantity: 1, unitPrice: 2500m, liters: 50m, costPerLiter);
        return (userId, voucherIds.Single());
    }

    /// <summary>
    /// A paid order of <paramref name="quantity"/> vouchers on one line - the shape a multi-pack has.
    /// </summary>
    private async Task<(Guid UserId, IReadOnlyList<Guid> VoucherIds)> SeedPackSaleAsync(
        ApplicationDbContext context,
        int quantity,
        decimal unitPrice,
        decimal liters,
        decimal costPerLiter)
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var now = TestClock.UtcNow;
        var voucherIds = Enumerable.Range(0, quantity).Select(_ => Guid.NewGuid()).ToList();

        context.Users.Add(new User
        {
            Id = userId,
            PhoneNumber = $"+38{userId:N}"[..20],
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });

        context.Orders.Add(new Order
        {
            Id = orderId,
            UserId = userId,
            Price = unitPrice * quantity,
            Status = OrderStatus.Fulfilled,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });

        // The line carries no SourceVoucherId: ck_order_line_renewal_pair_is_whole reserves that
        // column for the renewal branch, and forbids it on a plain first purchase. The voucher is
        // linked to its line through the custody movement's line_item_id instead - which is exactly
        // how FulfillmentService issues one, and why no amount is copied onto the movement.
        var lineId = Guid.NewGuid();
        context.OrderLineItems.Add(new OrderLineItem
        {
            Id = lineId,
            OrderId = orderId,
            SourceVoucherId = null,
            RenewalBranch = null,
            BrandId = BrandIds.ForSlug("okko"),
            Brand = "okko",
            FuelTypeId = ToUuid("okko-95"),
            Liters = liters,
            Quantity = quantity,
            UnitPrice = unitPrice,
            LineTotal = unitPrice * quantity,
        });

        foreach (var voucherId in voucherIds)
        {
            context.FuelVouchers.Add(new FuelVoucher
            {
                Id = voucherId,
                BrandId = BrandIds.ForSlug("okko"),
                Brand = "okko",
                FuelTypeId = ToUuid("okko-95"),
                Liters = liters,
                CostPerLiter = costPerLiter,
                SupplierExpirationDate = DateOnly.FromDateTime(now.AddMonths(1)),
                CustomerExpirationDate = DateOnly.FromDateTime(now.AddMonths(1)),
                VoucherNumber = $"OKKO-{voucherId:N}"[..20],
                QrPayload = $"payload-{voucherId:N}",
                Status = VoucherStatus.Assigned,
                AssignedToUserId = userId,
                OrderId = orderId,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });

            // The handover: pool -> customer, naming both the voucher and the line it was issued
            // against. This is what lets the posting find the price without guessing from fuel type.
            context.VoucherMovements.Add(new VoucherMovement
            {
                Id = Guid.CreateVersion7(),
                Kind = VoucherMovementKinds.Issued,
                VoucherId = voucherId,
                FromLocation = VoucherLocations.Pool,
                ToLocation = VoucherLocations.Customer,
                OrderId = orderId,
                LineItemId = lineId,
                ActorKind = VoucherActorKinds.User,
                ActorUserId = userId,
                OccurredAtUtc = now,
            });
        }

        await context.SaveChangesAsync();
        return (userId, voucherIds);
    }

    private async Task<Dictionary<string, (string Debit, string Credit, decimal Amount)>> PostAsync(
        Guid userId, Guid voucherId)
    {
        await using var context = CreateContext();
        var handler = new MarkVoucherAsUsedCommandHandler(
            context,
            TestRedemptions.For(context),
            NullLogger<MarkVoucherAsUsedCommandHandler>.Instance);

        var response = await handler.HandleAsync(new MarkVoucherAsUsedCommand(voucherId, userId));
        response.Success.Should().BeTrue();
        response.Message.Should().Be("Voucher reported; waiting for supplier confirmation");

        var confirmed = await context.ExecuteReplaySafeAsync(async ct =>
            await TestRedemptions.For(context).ConfirmAsync(voucherId, userId, "test confirmation", ct));
        confirmed.Outcome.Should().Be(RedemptionDecisionOutcome.Confirmed);

        await using var verify = CreateContext();
        var entries = await verify.JournalEntries
            .Include(e => e.Lines)
            .ThenInclude(l => l.Account)
            .Where(e => e.SourceId == voucherId)
            .ToListAsync();

        return entries.ToDictionary(
            e => e.SourceEvent,
            e => (
                e.Lines.Single(l => l.DebitUah > 0).Account!.Code,
                e.Lines.Single(l => l.CreditUah > 0).Account!.Code,
                e.Lines.Sum(l => l.DebitUah)));
    }

    [Fact]
    public async Task Redemption_Releases_The_Advance_Raises_Revenue_And_Pays_The_Supplier()
    {
        await using var setup = CreateContext();
        await ResetAsync(setup);
        var (userId, voucherId) = await SeedSaleAsync(setup, costPerLiter: 45m);

        var posted = await PostAsync(userId, voucherId);

        posted.Should().HaveCount(2, "revenue and cost of sales are two facts; the VAT was booked with the receipt");

        // The advance is released whole: 2500.00, VAT inside it. The VAT was already carved out when
        // the money arrived (payment.received.vat), so a redemption that split it again released the
        // liability and left the earned advance's VAT on 681 (planning #780).
        posted["voucher.redeemed.revenue"].Should().Be(("681", "702", 2500m));

        // 50 liters at 45.00 is what we actually paid, not today's offer price.
        posted["voucher.redeemed.cogs"].Should().Be(("808", "281", 2250m));

        // Gross revenue against gross cost. 250.00 is the sale's margin before VAT: the 416.67 of VAT
        // inside the 2500.00 is owed to the budget, not earned, and is on 643/704, not here.
        var margin = posted["voucher.redeemed.revenue"].Amount - posted["voucher.redeemed.cogs"].Amount;
        margin.Should().Be(250m);
    }

    [Fact]
    public async Task A_Voucher_On_A_Ten_Unit_Line_Books_One_Units_Revenue_Not_The_Whole_Line()
    {
        await using var setup = CreateContext();
        await ResetAsync(setup);

        // The 2026-10-29 production case: ten vouchers on one line at 862.00 each, 8620.00 paid.
        // Before #779 every one of them recognised the whole line - ten times the sale.
        var (userId, voucherIds) = await SeedPackSaleAsync(
            setup, quantity: 10, unitPrice: 862m, liters: 50m, costPerLiter: 45m);

        var posted = new List<Dictionary<string, (string Debit, string Credit, decimal Amount)>>();
        foreach (var voucherId in voucherIds)
        {
            posted.Add(await PostAsync(userId, voucherId));
        }

        // 862 is one voucher's share of the 8620.00 line, gross - the VAT was carved out of the
        // advance, so nothing is split here.
        posted.Should().OnlyContain(p => p["voucher.redeemed.revenue"].Amount == 862m);
        posted.Should().OnlyContain(p => !p.ContainsKey("voucher.redeemed.vat"));

        // The ten entries carry exactly what the customer paid for the line, with nothing left on 681.
        posted.Sum(p => p["voucher.redeemed.revenue"].Amount).Should().Be(8620m);
    }

    [Fact]
    public async Task A_Report_Writes_Drafts_And_Posts_Nothing_Until_The_Evidence_Is_Confirmed()
    {
        await using var setup = CreateContext();
        await ResetAsync(setup);
        var (userId, voucherId) = await SeedSaleAsync(setup, costPerLiter: 45m);

        await using (var context = CreateContext())
        {
            var handler = new MarkVoucherAsUsedCommandHandler(
                context,
                TestRedemptions.For(context),
                NullLogger<MarkVoucherAsUsedCommandHandler>.Instance);

            var report = await handler.HandleAsync(new MarkVoucherAsUsedCommand(voucherId, userId));
            report.Success.Should().BeTrue();
        }

        await using (var verify = CreateContext())
        {
            var voucher = await verify.FuelVouchers
                .Where(v => v.Id == voucherId)
                .Select(v => new { v.Status, v.UsedAtUtc })
                .SingleAsync();

            voucher.Status.Should().Be(VoucherStatus.PendingReconciliation);
            voucher.UsedAtUtc.Should().BeNull("a report is not a confirmation");

            var redemption = await verify.VoucherRedemptions
                .Where(r => r.VoucherId == voucherId)
                .SingleAsync();
            redemption.Status.Should().Be(VoucherRedemptionStatus.Reported);

            var entries = await verify.JournalEntries
                .Where(e => e.SourceId == voucherId)
                .ToListAsync();
            entries.Should().HaveCount(2);
            entries.Should().OnlyContain(e => e.Status == JournalEntryStatus.Draft);
        }

        await using (var context = CreateContext())
        {
            var confirmed = await context.ExecuteReplaySafeAsync(async ct =>
                await TestRedemptions.For(context).ConfirmAsync(voucherId, userId, "evidence accepted", ct));
            confirmed.Outcome.Should().Be(RedemptionDecisionOutcome.Confirmed);
        }

        await using var final = CreateContext();
        (await final.JournalEntries
                .Where(e => e.SourceId == voucherId)
                .Select(e => e.Status)
                .ToListAsync())
            .Should().OnlyContain(status => status == JournalEntryStatus.Posted);
    }

    [Fact]
    public async Task Every_Redemption_Entry_Is_Balanced_And_Posted()
    {
        await using var setup = CreateContext();
        await ResetAsync(setup);
        var (userId, voucherId) = await SeedSaleAsync(setup, costPerLiter: 45m);

        await PostAsync(userId, voucherId);

        await using var verify = CreateContext();
        var entries = await verify.JournalEntries
            .Include(e => e.Lines)
            .Where(e => e.SourceId == voucherId)
            .ToListAsync();

        entries.Should().OnlyContain(e => e.Status == JournalEntryStatus.Posted);
        entries.Should().OnlyContain(e => e.Lines.Sum(l => l.DebitUah) == e.Lines.Sum(l => l.CreditUah));
        entries.Should().OnlyContain(e => e.TamperHash != null);
    }

    [Fact]
    public async Task Marking_The_Same_Voucher_Used_Twice_Does_Not_Book_The_Sale_Twice()
    {
        await using var setup = CreateContext();
        await ResetAsync(setup);
        var (userId, voucherId) = await SeedSaleAsync(setup, costPerLiter: 45m);

        await PostAsync(userId, voucherId);

        await using var second = CreateContext();
        var handler = new MarkVoucherAsUsedCommandHandler(
            second,
            TestRedemptions.For(second),
            NullLogger<MarkVoucherAsUsedCommandHandler>.Instance);

        // The handler short-circuits on the idempotent branch, so this never reaches the posting.
        await handler.HandleAsync(new MarkVoucherAsUsedCommand(voucherId, userId));

        await using var verify = CreateContext();
        var count = await verify.JournalEntries.CountAsync(e => e.SourceId == voucherId);
        count.Should().Be(2);
    }

[Fact]
    public async Task A_Voucher_Issued_Without_A_Line_Still_Pays_The_Supplier_But_Recognises_No_Revenue()
    {
        // A held voucher always carries an order - ck_voucher_held_has_order refuses one without -
        // but the custody movement does not always name the line it settled, and LineItemId is
        // documented as being for the renewal paths. So the reachable gap between "we sold it" and
        // "here is the price" is a movement with no line, not a voucher with no order.
        //
        // Stock left the warehouse either way, so cost posts. The difference between cost and no
        // revenue is marketing spend, and which account that is belongs to the accountant - so the
        // handler logs it rather than inventing one, which is how the old TARGET_SCHEMA chart became
        // wrong in the first place.
        await using var setup = CreateContext();
        await ResetAsync(setup);

        var userId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var now = TestClock.UtcNow;

        setup.Users.Add(new User
        {
            Id = userId,
            PhoneNumber = $"+38{voucherId:N}"[..20],
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });

        setup.Orders.Add(new Order
        {
            Id = orderId,
            UserId = userId,
            Price = 1000m,
            Status = OrderStatus.Fulfilled,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });

        setup.FuelVouchers.Add(new FuelVoucher
        {
            Id = voucherId,
            BrandId = BrandIds.ForSlug("okko"),
            Brand = "okko",
            FuelTypeId = ToUuid("okko-95"),
            Liters = 10m,
            CostPerLiter = 45m,
            SupplierExpirationDate = DateOnly.FromDateTime(now.AddMonths(1)),
            CustomerExpirationDate = DateOnly.FromDateTime(now.AddMonths(1)),
            VoucherNumber = $"GIFT-{voucherId:N}"[..20],
            QrPayload = $"payload-{voucherId:N}",
            Status = VoucherStatus.Assigned,
            AssignedToUserId = userId,
            OrderId = orderId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });

        // A company gift: pool -> a worker, with no order line behind it. The actor is still named -
        // ck_voucher_movements_actor_named requires it, and an audit row that does not say who did
        // it is the thing that schema exists to prevent.
        setup.VoucherMovements.Add(new VoucherMovement
        {
            Id = Guid.CreateVersion7(),
            Kind = VoucherMovementKinds.Gift,
            VoucherId = voucherId,
            FromLocation = VoucherLocations.Pool,
            ToLocation = VoucherLocations.Worker,
            OrderId = orderId,
            LineItemId = null,
            ActorKind = VoucherActorKinds.User,
            ActorUserId = userId,
            OccurredAtUtc = now,
        });
        await setup.SaveChangesAsync();

        var posted = await PostAsync(userId, voucherId);

        posted.Should().HaveCount(1, "only cost of sales is knowable without a price to recognise");
        posted["voucher.redeemed.cogs"].Should().Be(("808", "281", 450m));
    }
}
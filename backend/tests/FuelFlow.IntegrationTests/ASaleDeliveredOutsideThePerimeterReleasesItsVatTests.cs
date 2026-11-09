using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Ledger;
using FuelFlow.Features.Orders.RefundOrder;
using FuelFlow.Features.Orders.SettlePaidOrder;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The receipt carves VAT out of every payment, and at that moment the platform does not know which
/// route the fuel will be delivered on — no line item names a supplier, and stock is picked by expiry
/// date. A sale delivered from stock bought outside the tax perimeter was therefore owing output VAT
/// on a supply that has no taxable event behind it.
/// </summary>
/// <remarks>
/// <para>
/// Each test drives the ordinary path — money, settle, fulfilment — because what matters is the
/// balance the path leaves on 643, not the arithmetic of one class. The unit price is 862.00 ₴ and
/// the rate is the standard 20 %: a two-unit order is 1 724.00 ₴, its carve is 287.33 ₴, and one unit
/// delivered outside the perimeter releases 143.67 ₴ of it. Those are figures an accountant can check
/// against a declaration.
/// </para>
/// <para>
/// The stock is seeded rather than imported, so the route is stated per voucher. In production the
/// route is copied onto the voucher from the supplier at import and is immutable from then on
/// (docs/ACQUISITION_CHANNELS.md §2), which is why nothing here reads a supplier at all.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class ASaleDeliveredOutsideThePerimeterReleasesItsVatTests : IClassFixture<TestDatabaseFixture>
{
    private const decimal UnitPrice = 862.00m;

    /// <summary>
    /// The liability account, named in the assertions on purpose: which account an event lands on is
    /// data (the mapping rule), and the number the VAT return is built from is the fact under test.
    /// </summary>
    private const string VatAccountCode = "643";

    private readonly TestDatabaseFixture _fixture;

    public ASaleDeliveredOutsideThePerimeterReleasesItsVatTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    /// <summary>
    /// The hole. Every unit the order got came in on the route outside the perimeter, so the VAT the
    /// receipt asserted is fully released — the state the sales model has been owing since the first
    /// partner import, and which nothing in the books could see.
    /// </summary>
    [Fact]
    public async Task ASaleDeliveredEntirelyOutsideThePerimeter_LeavesNoVatBehind()
    {
        var (orderId, userId, lineId) = Outfit();

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            AddPaidOrder(seed, orderId, userId, lineId, quantity: 2, delivered: false);
            AddAvailableStock(seed, userId, AcquisitionSources.Partner, dueInDays: 30);
            AddAvailableStock(seed, userId, AcquisitionSources.Partner, dueInDays: 60);
            await seed.SaveChangesAsync();
        }

        await using (var context = CreateContext())
            await SettleAsync(context, orderId);

        await using (var context = CreateContext())
            await FulfilAsync(context);

        await using var verify = CreateContext();

        (await ReleaseAmountAsync(verify, orderId)).Should().Be(287.33m,
            "the carve was 287.33 on 1 724.00 and every unit of it went out outside the perimeter");

        (await VatStillOwedAsync(verify, orderId)).Should().Be(0m,
            "the liability the receipt asserted does not stand for a supply it was never in");
    }

    /// <summary>
    /// The counterweight: a sale delivered from stock inside the perimeter was booked correctly, and a
    /// release that fired here would manufacture a VAT credit nobody was owed.
    /// </summary>
    [Fact]
    public async Task ASaleDeliveredInsideThePerimeter_ReleasesNothing()
    {
        var (orderId, userId, lineId) = Outfit();

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            AddPaidOrder(seed, orderId, userId, lineId, quantity: 2, delivered: false);
            AddAvailableStock(seed, userId, AcquisitionSources.Direct, dueInDays: 30);
            AddAvailableStock(seed, userId, AcquisitionSources.Direct, dueInDays: 60);
            await seed.SaveChangesAsync();
        }

        await using (var context = CreateContext())
            await SettleAsync(context, orderId);

        await using (var context = CreateContext())
            await FulfilAsync(context);

        await using var verify = CreateContext();

        (await ReleaseCountAsync(verify, orderId)).Should().Be(0,
            "stock bought inside the perimeter carries its VAT, and the receipt already said so");
        (await VatStillOwedAsync(verify, orderId)).Should().Be(287.33m);
    }

    /// <summary>
    /// The case the receipt could never have seen coming: one line, two routes. What comes off the
    /// liability is the outside part only, and the inside part stays owed.
    /// </summary>
    [Fact]
    public async Task AMixedSale_ReleasesOnlyThePartDeliveredOutside()
    {
        var (orderId, userId, lineId) = Outfit();

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            AddPaidOrder(seed, orderId, userId, lineId, quantity: 2, delivered: false);

            // Three units for a line that wants two, and the picking rule sorts by expiry — so the
            // delivery is one inside and one outside. Nothing written at checkout said either.
            AddAvailableStock(seed, userId, AcquisitionSources.Direct, dueInDays: 5);
            AddAvailableStock(seed, userId, AcquisitionSources.Partner, dueInDays: 20);
            AddAvailableStock(seed, userId, AcquisitionSources.Partner, dueInDays: 40);

            await seed.SaveChangesAsync();
        }

        await using (var context = CreateContext())
            await SettleAsync(context, orderId);

        await using (var context = CreateContext())
            await FulfilAsync(context);

        await using var verify = CreateContext();

        (await DeliveredRoutesAsync(verify, orderId)).Should().BeEquivalentTo(
            [AcquisitionSources.Direct, AcquisitionSources.Partner],
            "the mix this test is about is the picking rule's, not a filter anyone applied");

        (await ReleaseAmountAsync(verify, orderId)).Should().Be(143.67m, "one unit of 862.00 ₴");
        (await VatStillOwedAsync(verify, orderId)).Should().Be(143.66m,
            "the carve of 287.33 less the 143.67 released — the inside unit is still owed");
    }

    /// <summary>
    /// Idempotency, which is the property the correction rests on: a delivery that is replayed must
    /// not release twice, or the liability would go negative and nothing in the schema would notice.
    /// </summary>
    [Fact]
    public async Task AReleaseThatRunsAgain_BooksOneEntry()
    {
        var (orderId, userId, lineId) = Outfit();

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            AddPaidOrder(seed, orderId, userId, lineId, quantity: 1, delivered: false);
            AddAvailableStock(seed, userId, AcquisitionSources.Partner, dueInDays: 30);
            await seed.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            await SettleAsync(context, orderId);
            await FulfilAsync(context);

            // The same question again, as a redelivered event or a repaired order would ask it.
            var again = await LedgerTestDoubles.VatRelease(context).HandleAsync(
                new ReleaseVatOutsideThePerimeterCommand { OrderId = orderId });

            again.Should().Be(143.67m, "the answer to the question does not change");
        }

        await using var verify = CreateContext();

        (await ReleaseCountAsync(verify, orderId)).Should().Be(1,
            "the key is the event and the order, so the second run is handed the entry that exists");
    }

    /// <summary>
    /// A delivery that names no line cannot be valued, and valuing only the rows that do would book a
    /// confident number for a delivery whose size is not actually known. It releases nothing.
    /// </summary>
    [Fact]
    public async Task ADeliveryThatNamesNoLine_ReleasesNothing()
    {
        var (orderId, userId, lineId) = Outfit();

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            AddPaidOrder(seed, orderId, userId, lineId, quantity: 2, delivered: false);
            await seed.SaveChangesAsync();
        }

        await using (var context = CreateContext())
            await SettleAsync(context, orderId);

        await using (var legacy = CreateContext())
        {
            // The shape the rows had before the line column existed — and the shape the backfill
            // deliberately left the ambiguous ones in.
            AddDeliveredStock(legacy, orderId, userId, lineId: null, AcquisitionSources.Partner, dueInDays: 30);
            AddDeliveredStock(legacy, orderId, userId, lineId: null, AcquisitionSources.Partner, dueInDays: 60);
            await legacy.SaveChangesAsync();
        }

        await using var releaser = CreateContext();
        var released = await LedgerTestDoubles.VatRelease(releaser).HandleAsync(
            new ReleaseVatOutsideThePerimeterCommand { OrderId = orderId });

        released.Should().Be(0m, "a delivery nobody can value is not a number to guess at");

        await using var verify = CreateContext();
        (await ReleaseCountAsync(verify, orderId)).Should().Be(0);
    }

    /// <summary>
    /// The ceiling from the other side: an order whose receipt booked no VAT has nothing to release,
    /// so the correction cannot manufacture a credit against a liability that was never asserted.
    /// </summary>
    [Fact]
    public async Task AReceiptThatBookedNoVat_HoldsNothingToRelease()
    {
        var (orderId, userId, lineId) = Outfit();

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);

            // Delivered without ever being settled: the status written by hand, which is the shape
            // S-1 had and the shape a repair script can still make. The money row is there; the
            // books are not.
            AddPaidOrder(seed, orderId, userId, lineId, quantity: 2, delivered: true);
            AddDeliveredStock(seed, orderId, userId, lineId, AcquisitionSources.Partner, dueInDays: 30);
            AddDeliveredStock(seed, orderId, userId, lineId, AcquisitionSources.Partner, dueInDays: 60);

            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext();
        var released = await LedgerTestDoubles.VatRelease(context).HandleAsync(
            new ReleaseVatOutsideThePerimeterCommand { OrderId = orderId });

        released.Should().Be(0m, "nothing was carved, so there is nothing here that can correct it");

        await using var verify = CreateContext();
        (await ReleaseCountAsync(verify, orderId)).Should().Be(0);
    }

    /// <summary>
    /// A promo order has no carve to release: the 2026-11-07 ruling makes the promotional sale book
    /// nothing at all, receipt and VAT leg included. An empty carve is its expected state, so the
    /// handler answers zero instead of calling a book to be repaired.
    /// </summary>
    [Fact]
    public async Task APromoOrderBookedNoVat_SoNothingIsReleased()
    {
        var (orderId, userId, lineId) = Outfit();

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            AddPaidOrder(seed, orderId, userId, lineId, quantity: 2, delivered: true,
                settlementRoute: SettlementRoute.Declared);
            AddDeliveredStock(seed, orderId, userId, lineId, AcquisitionSources.Partner, dueInDays: 30);
            AddDeliveredStock(seed, orderId, userId, lineId, AcquisitionSources.Partner, dueInDays: 60);

            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext();
        var released = await LedgerTestDoubles.VatRelease(context).HandleAsync(
            new ReleaseVatOutsideThePerimeterCommand { OrderId = orderId });

        released.Should().Be(0m, "a promo sale books nothing by decision, so there is no carve to correct");

        await using var verify = CreateContext();
        (await ReleaseCountAsync(verify, orderId)).Should().Be(0);
    }

    /// <summary>
    /// The two corrections on one order are two divisions of one carve, and two divisions of the
    /// halves can round a kopeck past the single division the receipt made: 1 724.00 ₴ carves 287.33,
    /// while 862.00 ₴ returned and 862.00 ₴ delivered each divide to 143.67. Whichever runs second
    /// takes what remains, so 643 closes at zero instead of carrying a kopeck of a liability that was
    /// never asserted.
    /// </summary>
    [Fact]
    public async Task AReturnedUnitAndADeliveredUnit_CloseTheCarveExactly()
    {
        var (orderId, userId, lineId) = Outfit();

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            AddPaidOrder(seed, orderId, userId, lineId, quantity: 2, delivered: false);
            AddAvailableStock(seed, userId, AcquisitionSources.Partner, dueInDays: 30);
            AddAvailableStock(seed, userId, AcquisitionSources.Partner, dueInDays: 60);
            await seed.SaveChangesAsync();
        }

        await using (var context = CreateContext())
            await SettleAsync(context, orderId);

        // One voucher was never delivered: the customer was refunded for it, and the acquirer
        // confirmed the money went back before the other unit was handed over.
        await using (var returned = CreateContext())
            await BookConfirmedRefundAsync(returned, orderId, userId, UnitPrice);

        await using (var context = CreateContext())
            await FulfilAsync(context);

        await using var verify = CreateContext();

        (await VatStillOwedAsync(verify, orderId)).Should().Be(0m,
            "the refund divided 143.67 and the release took the remaining 143.66 of the 287.33 carve");
        (await ReleaseAmountAsync(verify, orderId)).Should().Be(143.66m,
            "the last correction takes what remains, not its own division of the half");
    }

    /// <summary>
    /// The other side of the cap: a return on an order whose receipt booked no VAT releases nothing.
    /// One refund per order is a schema fact (<c>ux_payments_one_refund_per_order</c>), so the
    /// two-divisions-of-one-carve case is a refund plus a delivery, not two refund rows. This is the
    /// case where there is no carve at all - a promo sale books nothing by decision - and the refund
    /// must not manufacture a credit against a liability the receipt never asserted.
    /// </summary>
    [Fact]
    public async Task ARefundOnAnOrderWhoseReceiptAssertedNoVat_BooksNoVatLeg()
    {
        var (orderId, userId, lineId) = Outfit();

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            AddPaidOrder(seed, orderId, userId, lineId, quantity: 1, delivered: false);
            await seed.SaveChangesAsync();
        }

        // No settle: the receipt this order would have posted was never booked, so 643 holds
        // nothing of this order's money.
        await using (var returned = CreateContext())
            await BookConfirmedRefundAsync(returned, orderId, userId, UnitPrice);

        await using var verify = CreateContext();

        (await RefundVatAmountsAsync(verify, orderId)).Should().BeEmpty(
            "the refund divides 143.67, but there is no carve on 643 to take it from");
        (await VatStillOwedAsync(verify, orderId)).Should().Be(0m,
            "the books end where they began, instead of carrying a credit nobody asserted");
    }

    // ---------------------------------------------------------------- fixtures

    private static (Guid OrderId, Guid UserId, Guid LineId) Outfit()
        => (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    /// <summary>
    /// Two units of OKKO A-95 at 10 L, with the money that pays for them. With
    /// <paramref name="delivered"/> the order is already handed over — which is how a fulfilment is
    /// replayed — and otherwise it waits for stock.
    /// </summary>
    private static void AddPaidOrder(
        ApplicationDbContext context, Guid orderId, Guid userId, Guid lineId,
        int quantity, bool delivered, SettlementRoute? settlementRoute = null)
    {
        context.Users.Add(new User
        {
            Id = userId,
            PhoneNumber = $"+38{userId:N}"[..20],
            IsActive = true,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow,
        });

        var price = UnitPrice * quantity;

        context.Orders.Add(new Order
        {
            Id = orderId,
            UserId = userId,
            Price = price,
            SettlementRoute = settlementRoute,
            Status = delivered ? OrderStatus.Fulfilled : OrderStatus.PendingPayment,
            FulfilledAtUtc = delivered ? TestClock.UtcNow : null,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow,
            LineItems =
            [
                new OrderLineItem
                {
                    Id = lineId,
                    OrderId = orderId,
                    BrandId = BrandIds.ForSlug("okko"),
                    Brand = "okko",
                    FuelTypeId = ToUuid("okko-95"),
                    Liters = 10m,
                    Quantity = quantity,
                    UnitPrice = UnitPrice,
                    LineTotal = price,
                },
            ],
        });

        context.Payments.Add(new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            Kind = PaymentKind.Capture,
            Provider = "monobank",
            ProviderInvoiceId = $"INV-{orderId:N}",
            AmountUah = price,
            AmountKopecks = (int)(price * 100m),
            Currency = "UAH",
            Status = PaymentStatus.Succeeded,
            PaidAtUtc = TestClock.UtcNow,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow,
        });
    }

    private static void AddAvailableStock(
        ApplicationDbContext context, Guid userId, string route, int dueInDays)
    {
        var voucher = Stock(userId, route, dueInDays);
        voucher.Status = VoucherStatus.Available;
        context.FuelVouchers.Add(voucher);
    }

    /// <summary>
    /// A voucher already in the customer's hand and recorded against the order — <paramref
    /// name="lineId"/> null is the legacy shape, and the point of <see
    /// cref="ADeliveryThatNamesNoLine_ReleasesNothing"/>.
    /// </summary>
    private static void AddDeliveredStock(
        ApplicationDbContext context, Guid orderId, Guid userId, Guid? lineId, string route, int dueInDays)
    {
        var voucher = Stock(userId, route, dueInDays);
        voucher.Status = VoucherStatus.Assigned;
        voucher.OrderId = orderId;
        voucher.AssignedToUserId = userId;

        context.FuelVouchers.Add(voucher);
        context.Fulfillments.Add(new Fulfillment
        {
            OrderId = orderId,
            VoucherId = voucher.Id,
            LineItemId = lineId,
            FulfilledAtUtc = voucher.UpdatedAtUtc,
        });
    }

    private static FuelVoucher Stock(Guid userId, string route, int dueInDays)
    {
        var nonce = $"{route}-{Guid.NewGuid():N}";
        var due = DateOnly.FromDateTime(TestClock.UtcNow).AddDays(dueInDays);

        return new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Brand = "okko",
            BrandId = BrandIds.ForSlug("okko"),
            FuelTypeId = ToUuid("okko-95"),
            Liters = 10m,
            SupplierExpirationDate = due,
            CustomerExpirationDate = due,
            VoucherNumber = nonce,
            QrPayload = nonce,
            Status = VoucherStatus.Available,
            AcquisitionSource = route,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow,
        };
    }

    /// <summary>
    /// The settle the money path uses: it checks the captures on the order and, if they pay for it,
    /// books the receipt — the carve this suite exists to correct.
    /// </summary>
    private static async Task SettleAsync(ApplicationDbContext context, Guid orderId)
    {
        var result = await TestJournal.TrySettleFor(context).HandleAsync(new TrySettlePaidOrderCommand
        {
            OrderId = orderId,
            Because = "the test declared the money and attached it",
        });

        result.Settled.Should().BeTrue(result.Reason ?? "the money on the order pays for it");
    }

    /// <summary>
    /// The confirmation path the sync runs: a refund the acquirer reported as returned, booked
    /// through the poster the sync itself calls.
    /// </summary>
    private static async Task BookConfirmedRefundAsync(
        ApplicationDbContext context, Guid orderId, Guid userId, decimal amountUah)
    {
        var captureId = await context.Payments.AsNoTracking()
            .Where(p => p.OrderId == orderId && p.Kind == PaymentKind.Capture)
            .Select(p => p.Id)
            .FirstAsync();

        var refund = new Payment
        {
            Id = Guid.CreateVersion7(),
            OrderId = orderId,
            UserId = userId,
            Kind = PaymentKind.Refund,
            ParentPaymentId = captureId,
            Provider = "monobank",
            AmountUah = amountUah,
            AmountKopecks = (int)(amountUah * 100m),
            Currency = "UAH",
            Status = PaymentStatus.Succeeded,
            PaidAtUtc = TestClock.UtcNow,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow,
        };

        context.Payments.Add(refund);
        await context.SaveChangesAsync();

        var order = await context.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        var poster = new RefundJournalPoster(
            new JournalPostingService(context, NullLogger<JournalPostingService>.Instance),
            context,
            NullLogger<RefundJournalPoster>.Instance);

        await poster.BookConfirmedRefundAsync(refund, order);
    }

    private static async Task FulfilAsync(ApplicationDbContext context)
    {        var monobankMock = new Mock<IMonobankClient>();
        monobankMock
            .Setup(m => m.CancelInvoiceAsync(
                It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<MonobankBasketItem>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonobankCancelResponse { Status = "processing" });

        var service = new FulfillmentService(
            context,
            NullLogger<FulfillmentService>.Instance,
            new RefundOrderCommandHandler(
                context, new StubMonobankClientFactory(monobankMock.Object), new AuditEventService(context)),
            new RuntimeSettingsService(context),
            NotificationDispatcher.Disabled,
            new ConfigurationBuilder().Build(),
            LedgerTestDoubles.VatRelease(context));

        await service.ProcessPendingOrdersAsync();
    }

    // ---------------------------------------------------------------- reading the books back

    /// <summary>The amount the release booked for this order, or 0 when it booked none.</summary>
    private static async Task<decimal> ReleaseAmountAsync(ApplicationDbContext context, Guid orderId)
        => await (from line in context.JournalLines.AsNoTracking()
                  join entry in context.JournalEntries.AsNoTracking() on line.EntryId equals entry.Id
                  where entry.SourceEvent == ReleaseVatOutsideThePerimeterCommandHandler.ReleaseEvent
                       && entry.SourceId == orderId && line.LineNo == 1
                  select line.DebitUah)
                 .SingleOrDefaultAsync();

    private static async Task<int> ReleaseCountAsync(ApplicationDbContext context, Guid orderId)
        => await context.JournalEntries.AsNoTracking()
            .CountAsync(e => e.SourceEvent == ReleaseVatOutsideThePerimeterCommandHandler.ReleaseEvent
                          && e.SourceId == orderId);

    /// <summary>
    /// What the order still leaves owed on 643: credits less debits, so the answer is the figure a
    /// VAT return would carry for it. The order's refunds are part of that figure - their legs are
    /// keyed on the refund row, not on the order - which is why this reads both.
    /// </summary>
    private static async Task<decimal> VatStillOwedAsync(ApplicationDbContext context, Guid orderId)
    {
        var refundIds = await context.Payments
            .AsNoTracking()
            .Where(p => p.OrderId == orderId && p.Kind == PaymentKind.Refund)
            .Select(p => p.Id)
            .ToListAsync();

        return await (from line in context.JournalLines.AsNoTracking()
                      join entry in context.JournalEntries.AsNoTracking() on line.EntryId equals entry.Id
                      join account in context.ChartOfAccounts.AsNoTracking() on line.AccountId equals account.Id
                      where (entry.SourceId == orderId
                             || (entry.SourceId != null && refundIds.Contains(entry.SourceId.Value)))
                            && account.Code == VatAccountCode
                      select line.CreditUah - line.DebitUah)
                     .SumAsync();
    }

    /// <summary>What each of the order's refunds released on 643, in booking order.</summary>
    private static async Task<List<decimal>> RefundVatAmountsAsync(
        ApplicationDbContext context, Guid orderId)
        => await (from line in context.JournalLines.AsNoTracking()
                  join entry in context.JournalEntries.AsNoTracking() on line.EntryId equals entry.Id
                  where entry.SourceEvent == RefundJournalPoster.RefundedVatEvent
                        && entry.SourceId != null
                        && context.Payments.Any(p => p.Id == entry.SourceId
                                                  && p.OrderId == orderId
                                                  && p.Kind == PaymentKind.Refund)
                  orderby entry.EntryNo
                  select line.DebitUah)
                 .ToListAsync();

    /// <summary>Which routes the vouchers actually handed over came in on.</summary>
    private static async Task<List<string>> DeliveredRoutesAsync(ApplicationDbContext context, Guid orderId)
        => await (from fulfillment in context.Fulfillments.AsNoTracking()
                  join voucher in context.FuelVouchers.AsNoTracking()
                      on fulfillment.VoucherId equals voucher.Id
                  where fulfillment.OrderId == orderId
                  select voucher.AcquisitionSource)
                 .ToListAsync();

    private static async Task ResetAsync(ApplicationDbContext context)
        => await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "payments", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");
}

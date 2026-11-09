using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Ledger;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Features.Orders.RefundOrder;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.Payments.SharedModels;
using FuelFlow.SharedKernel;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Covers the manual refund lifecycle against a real PostgreSQL container:
/// admin-initiated refund -> Monobank cancel call -> RefundStatusSyncService
/// confirmation/failure -> order status flip. The automatic refund path is
/// covered by FulfillmentConcurrencyIntegrationTests.
/// </summary>
[Collection("Integration Tests")]
public sealed class RefundIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public RefundIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ManualRefund_WhenMonobankConfirms_ShouldCompleteRefundAndFlipOrderToRefunded()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var invoiceId = $"test-invoice-{orderId:N}";

        await SeedAsync(seed =>
        {
            seed.Users.Add(CreateUser(userId));
            AddPaidOrder(seed, CreateOrder(orderId, userId, invoiceId, OrderStatus.PendingFulfillment,
                quantity: 2, unitPrice: 1000), invoiceId);
        });

        var monobankMock = CreateMonobankMock(out var cancelledAmount);
        monobankMock.Setup(m => m.GetInvoiceStatusAsync(invoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonobankInvoiceStatus
            {
                InvoiceId = invoiceId,
                Status = "success",
                CancelList = new List<MonobankCancelListItem>
                {
                    new()
                    {
                        Status = "success",
                        Amount = 200000,
                        ExtRef = orderId.ToString(),
                        ModifiedDate = TestClock.UtcNow
                    }
                }
            });

        RefundOrderResult result;
        using (var context = CreateContext())
        {
            var handler = new RefundOrderCommandHandler(context, new StubMonobankClientFactory(monobankMock.Object), new AuditEventService(context));
            result = await handler.HandleAsync(new RefundOrderCommand
            {
                OrderId = orderId,
                IsAutomatic = false,
                ChangedByUserName = "admin-test"
            });
        }

        result.Status.Should().Be("Processing");
        result.AmountKopecks.Should().Be(200000);
        cancelledAmount.Value.Should().Be(200000);

        // While the refund is in flight the order must keep its fulfillment-derived status.
        using (var mid = CreateContext())
        {
            var order = await mid.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
            order.Status.Should().Be(OrderStatus.PendingFulfillment);

            var refund = await mid.Payments.AsNoTracking().SingleAsync(r => r.OrderId == orderId && r.Kind == PaymentKind.Refund);
            refund.Status.Should().Be(PaymentStatus.Pending);
            refund.IsAutomatic.Should().BeFalse();
            refund.CreatedByUserName.Should().Be("admin-test");
        }

        using (var syncContext = CreateContext())
        {
            var syncService = new RefundStatusSyncService(
                syncContext, new StubMonobankClientFactory(monobankMock.Object), NullLogger<RefundStatusSyncService>.Instance);
            await syncService.SyncPendingRefundsAsync();
        }

        using (var verify = CreateContext())
        {
            var refund = await verify.Payments.AsNoTracking().SingleAsync(r => r.OrderId == orderId && r.Kind == PaymentKind.Refund);
            refund.Status.Should().Be(PaymentStatus.Succeeded);
            // The brand's own word is no longer stored on the refund - it is a typed status plus the
            // acquirer's row in webhook_events. What matters here is the money moved, and it did.

            var order = await verify.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
            order.Status.Should().Be(OrderStatus.Refunded);

            // And the books moved with it: the advance leaves 681 and the bank account leaves 311, and
            // the VAT the receipt carved out of that same money goes back to the budget's side. Two
            // legs, because the receipt that brought the money in was two legs.
            var booked = await BookedRefundLegsAsync(refund.Id);
            booked.Should().HaveCount(2, "a confirmed refund is one fact, and it has the same two legs as the receipt it mirrors");
            booked.Should().Contain(("payment.refunded", "681", "311", 2000.00m));
            booked.Should().Contain(("payment.refunded.vat", "643", "704", 333.33m));
        }
    }

    /// <summary>
    /// The webhook route confirms the refund, and the posting that follows it runs once.
    /// </summary>
    [Fact]
    public async Task ManualRefund_ConfirmedByTheWebhookCancelList_BooksTheRefundExactlyOnce()
    {
        // The poller above is one route; the webhook push is the other and the one that usually fires
        // first. Both have to book, and neither may book twice: the second confirmation of the same
        // refund reaching the ledger is what a poller and a webhook racing inside one tick looks like.
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var invoiceId = $"test-invoice-{orderId:N}";

        await SeedAsync(seed =>
        {
            seed.Users.Add(CreateUser(userId));
            AddPaidOrder(seed, CreateOrder(orderId, userId, invoiceId, OrderStatus.PendingFulfillment,
                quantity: 1, unitPrice: 1000), invoiceId);
        });

        var cancelList = new List<MonobankCancelListItem>
        {
            new()
            {
                Status = "success",
                Amount = 100000,
                ExtRef = orderId.ToString(),
                ModifiedDate = TestClock.UtcNow
            }
        };

        var monobankMock = CreateMonobankMock(out _);
        using (var context = CreateContext())
        {
            var handler = new RefundOrderCommandHandler(context, new StubMonobankClientFactory(monobankMock.Object), new AuditEventService(context));
            await handler.HandleAsync(new RefundOrderCommand { OrderId = orderId, ChangedByUserName = "admin-test" });
        }

        // The refund id, read while it is still Pending: the ledger key is this row, so the test
        // needs to know which row the booking is expected to hang off.
        Guid refundId;
        using (var read = CreateContext())
        {
            refundId = (await read.Payments.AsNoTracking()
                .SingleAsync(r => r.OrderId == orderId && r.Kind == PaymentKind.Refund)).Id;
        }

        using (var syncContext = CreateContext())
        {
            var syncService = new RefundStatusSyncService(
                syncContext, new StubMonobankClientFactory(monobankMock.Object), NullLogger<RefundStatusSyncService>.Instance);
            await syncService.SyncRefundForInvoiceAsync(invoiceId, cancelList);

            var refund = await syncContext.Payments.AsNoTracking()
                .SingleAsync(r => r.OrderId == orderId && r.Kind == PaymentKind.Refund);
            refund.Status.Should().Be(PaymentStatus.Succeeded);
        }

        var booked = await BookedRefundLegsAsync(refundId);
        booked.Should().HaveCount(2);
        booked.Should().Contain(("payment.refunded", "681", "311", 1000.00m));
        booked.Should().Contain(("payment.refunded.vat", "643", "704", 166.67m));

        // The booking call reached a second time for the same refund — what happens when the poller
        // and the webhook both confirm one cancel within the same tick. The idempotency key is the
        // refund row, so the second call must return the entries that exist rather than add another.
        using (var race = CreateContext())
        {
            var refund = await race.Payments.AsNoTracking().SingleAsync(p => p.Id == refundId);
            var order = await race.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
            var poster = new RefundJournalPoster(
                new JournalPostingService(race, NullLogger<JournalPostingService>.Instance),
                race,
                NullLogger<RefundJournalPoster>.Instance);

            var again = await poster.BookConfirmedRefundAsync(refund, order);
            again.Should().NotBeNull();

            var entryCount = await race.JournalEntries.AsNoTracking()
                .CountAsync(e => e.SourceType == JournalSourceType.Refund && e.SourceId == refundId);
            entryCount.Should().Be(2, "a refund that arrives twice is still one fact about money, and it has two legs");
        }

        (await BookedRefundLegsAsync(refundId))
            .Should().HaveCount(2, "the second arrival added nothing to the books");
    }

    [Fact]
    public async Task ManualRefund_OrderChangedAfterTheBankRefundedIt_KeepsTheBankedFigureAndSaysSo()
    {
        // #856. The entry is keyed on the refund row and the engine refuses to write that key twice, so
        // re-pricing a confirmed refund could never re-book it: the journal kept the figure the bank
        // returned, the row kept a newer one, and the handler still reported the row as "Completed". A
        // sum the acquirer moved is not recomputed here any more. What the handler may not do is stay
        // silent - it writes both figures and what the books hold, and a person decides what the gap means.
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var invoiceId = $"test-invoice-{orderId:N}";

        await SeedAsync(seed =>
        {
            seed.Users.Add(CreateUser(userId));
            AddPaidOrder(seed, CreateOrder(orderId, userId, invoiceId, OrderStatus.PendingFulfillment,
                quantity: 2, unitPrice: 1000), invoiceId);
        });

        var monobankMock = CreateMonobankMock(out _);
        monobankMock.Setup(m => m.GetInvoiceStatusAsync(invoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonobankInvoiceStatus
            {
                InvoiceId = invoiceId,
                Status = "success",
                CancelList = new List<MonobankCancelListItem>
                {
                    new()
                    {
                        Status = "success",
                        Amount = 200000,
                        ExtRef = orderId.ToString(),
                        ModifiedDate = TestClock.UtcNow
                    }
                }
            });

        using (var context = CreateContext())
        {
            var handler = new RefundOrderCommandHandler(context, new StubMonobankClientFactory(monobankMock.Object), new AuditEventService(context));
            await handler.HandleAsync(new RefundOrderCommand { OrderId = orderId, ChangedByUserName = "admin-test" });
        }

        Guid refundId;
        using (var syncContext = CreateContext())
        {
            var syncService = new RefundStatusSyncService(
                syncContext, new StubMonobankClientFactory(monobankMock.Object), NullLogger<RefundStatusSyncService>.Instance);
            await syncService.SyncPendingRefundsAsync();

            refundId = (await syncContext.Payments.AsNoTracking()
                .SingleAsync(r => r.OrderId == orderId && r.Kind == PaymentKind.Refund)).Id;
        }

        var bookedBeforeTheSecondPress = await BookedRefundLegsAsync(refundId);
        bookedBeforeTheSecondPress.Should().Contain(("payment.refunded", "681", "311", 2000.00m));
        bookedBeforeTheSecondPress.Should().Contain(("payment.refunded.vat", "643", "704", 333.33m));

        // One of the two units is delivered after the money came back, so the order now shows 1 000 UAH
        // still undelivered against a refund the bank closed at 2 000 UAH. The second press reports that
        // and changes nothing. Not SeedAsync: that truncates and would wipe the refund and its entries.
        using (var deliver = CreateContext())
        {
            var voucher = new FuelVoucher
            {
                Id = Guid.NewGuid(),
                BrandId = BrandIds.ForSlug("okko"),
                Brand = "okko",
                FuelTypeId = ToUuid("okko-dp"),
                Liters = 10m,
                SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow).AddMonths(1),
                CustomerExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow).AddMonths(1),
                VoucherNumber = $"OC-{orderId:N}-drift",
                QrPayload = $"payload-{orderId:N}-drift",
                Status = VoucherStatus.Assigned,
                AssignedToUserId = userId,
                OrderId = orderId,
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            };
            deliver.FuelVouchers.Add(voucher);
            deliver.Fulfillments.Add(new Fulfillment
            {
                OrderId = orderId,
                VoucherId = voucher.Id,
                FulfilledAtUtc = TestClock.UtcNow
            });
            await deliver.SaveChangesAsync();
        }

        using (var context = CreateContext())
        {
            var handler = new RefundOrderCommandHandler(context, new StubMonobankClientFactory(monobankMock.Object), new AuditEventService(context));
            await handler.HandleAsync(new RefundOrderCommand { OrderId = orderId, ChangedByUserName = "admin-test" });
        }

        using (var verify = CreateContext())
        {
            var refund = await verify.Payments.AsNoTracking().SingleAsync(p => p.Id == refundId);
            refund.AmountUah.Should().Be(2000.00m,
                "the acquirer moved 2 000.00 UAH; a delivery that landed afterwards does not rewrite that");

            var report = await verify.Set<AuditEventOutbox>().AsNoTracking()
                .SingleAsync(e => e.EventType == RefundAmountDisagreement.EventName);
            report.AggregateId.Should().Be(refundId.ToString());
            report.Summary.Should().Contain("1000.00 UAH still undelivered");
            report.Summary.Should().Contain("a difference of -1000.00 UAH");
            report.Summary.Should().Contain("is not rewritten");
            report.Summary.Should().NotContain("Nothing is booked",
                "the confirmation booked this refund — that sentence is for one the books never saw");
            report.OldValue.Should().Contain("2000");
            report.NewValue.Should().Contain("1000");
            report.NewValue.Should().NotContain("2000");

            // And the entries the bank's confirmation wrote are untouched - the gap is reported, not
            // papered over by editing an accounting fact that is already posted.
            var bookedAfterTheSecondPress = await BookedRefundLegsAsync(refundId);
            bookedAfterTheSecondPress.Should().Contain(("payment.refunded", "681", "311", 2000.00m));
            bookedAfterTheSecondPress.Should().Contain(("payment.refunded.vat", "643", "704", 333.33m));
        }
    }

    [Fact]
    public async Task ManualRefund_WhenOrderHasLongIdempotencyKey_ShouldNotOverflowExtRef()
    {
        // Regression (#63): the refund copies the order's IdempotencyKey into refunds.ext_ref.
        // A bulk checkout builds a 111-char key (userId + time bucket + cart digest + a fresh
        // GUID), which fits orders.idempotency_key (varchar 150) but overflowed the old
        // ext_ref (varchar 100) — the INSERT threw Postgres 22001 "value too long for type
        // character varying(100)", surfacing as an unhandled 500 on every bulk-order refund.
        // ext_ref is now varchar(150), matching its source column.
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var invoiceId = $"test-invoice-{orderId:N}";

        // Same length (111) a real BulkCheckoutCommandHandler idempotency key has.
        var longIdempotencyKey =
            $"{userId:N}:{TestClock.UtcNow:yyyyMMddHH}00:{Guid.NewGuid():N}:{Guid.NewGuid():N}";
        longIdempotencyKey.Length.Should().BeGreaterThan(100);

        await SeedAsync(seed =>
        {
            seed.Users.Add(CreateUser(userId));
            var order = CreateOrder(orderId, userId, invoiceId, OrderStatus.PendingFulfillment,
                quantity: 2, unitPrice: 1000);
            order.IdempotencyKey = longIdempotencyKey;
            seed.Orders.Add(order);
            seed.Payments.Add(SucceededCaptureFor(order, invoiceId));
        });

        var monobankMock = CreateMonobankMock(out _);

        RefundOrderResult result;
        using (var context = CreateContext())
        {
            var handler = new RefundOrderCommandHandler(context, new StubMonobankClientFactory(monobankMock.Object), new AuditEventService(context));
            result = await handler.HandleAsync(new RefundOrderCommand
            {
                OrderId = orderId,
                IsAutomatic = false,
                ChangedByUserName = "admin-test"
            });
        }

        result.Status.Should().Be("Processing");

        using (var verify = CreateContext())
        {
            var refund = await verify.Payments.AsNoTracking().SingleAsync(r => r.OrderId == orderId && r.Kind == PaymentKind.Refund);
            refund.Status.Should().Be(PaymentStatus.Pending);

            // The overflow this test guards against was refunds.ext_ref, a varchar(150) copy of this
            // key. There is no copy any more - the reference is read from the order at match time, so
            // the column's width cannot bite. The key itself still has to fit, because that is what
            // the acquirer echoes back.
            (await verify.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId))
                .IdempotencyKey.Should().Be(longIdempotencyKey);
        }
    }

/// <summary>
    /// An order that predates the payments table still gets refunded - and its capture is promoted.
    /// </summary>
/// <remarks>
/// Such an order carries its payment in four columns of its own: invoice id, brand status, price.
/// Those are real evidence, so the handler writes a proper capture row from them rather than
/// refusing. Refusing would mean a genuinely paid customer could not be refunded, which is the worst
/// answer available. Production holds no orders at all, so this path is defensive in practice; the
/// test exists because the behaviour is not obvious from the code that takes it.
/// </remarks>
[Fact]
    public async Task RefundOfAnOrderWithNoCaptureRow_ShouldPromoteItsColumnsIntoACapture()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var invoiceId = $"test-invoice-{orderId:N}";

        await SeedAsync(seed =>
        {
            seed.Users.Add(CreateUser(userId));
            // Deliberately no capture: the pre-payments-table shape.
            seed.Orders.Add(CreateOrder(orderId, userId, invoiceId, OrderStatus.PendingFulfillment,
                quantity: 2, unitPrice: 1000));
        });

        var monobankMock = CreateMonobankMock(out _);
        var command = new RefundOrderCommand { OrderId = orderId, ChangedByUserName = "admin-test" };

        using (var context = CreateContext())
        {
            var handler = new RefundOrderCommandHandler(
                context, new StubMonobankClientFactory(monobankMock.Object), new AuditEventService(context));
            var result = await handler.HandleAsync(command);
            result.Status.Should().Be("Processing");
        }

        using (var verify = CreateContext())
        {
            // The capture now exists, is a real one, and carries the order's own amount.
            var capture = verify.Payments.Single(p => p.OrderId == orderId && p.Kind == PaymentKind.Capture);
            capture.Status.Should().Be(PaymentStatus.Succeeded);
            capture.AmountUah.Should().Be(2000m);
            capture.ProviderInvoiceId.Should().Be(invoiceId);

            // And the refund points at it, which is the whole point of the exercise.
            var refundPayment = verify.Payments.Single(p => p.OrderId == orderId && p.Kind == PaymentKind.Refund);
            refundPayment.ParentPaymentId.Should().Be(capture.Id);
        }
    }

    [Fact]
    public async Task ManualRefund_WhenRequestedTwice_ShouldReuseSingleRefundAndCancelOnce()
    {
        // Regression (#53/R3): a repeat/double-click must not open a second refund row or
        // fire a second Monobank cancel (unique index on order_id + the in-flight guard).
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var invoiceId = $"test-invoice-{orderId:N}";

        await SeedAsync(seed =>
        {
            seed.Users.Add(CreateUser(userId));
            AddPaidOrder(seed, CreateOrder(orderId, userId, invoiceId, OrderStatus.PendingFulfillment,
                quantity: 2, unitPrice: 1000), invoiceId);
        });

        var monobankMock = CreateMonobankMock(out _);
        var command = new RefundOrderCommand { OrderId = orderId, ChangedByUserName = "admin-test" };

        RefundOrderResult first;
        using (var context = CreateContext())
        {
            var handler = new RefundOrderCommandHandler(context, new StubMonobankClientFactory(monobankMock.Object), new AuditEventService(context));
            first = await handler.HandleAsync(command);
        }

        using (var context = CreateContext())
        {
            var handler = new RefundOrderCommandHandler(context, new StubMonobankClientFactory(monobankMock.Object), new AuditEventService(context));
            var second = await handler.HandleAsync(command);
            second.RefundId.Should().Be(first.RefundId); // same row, not a new one
            second.Status.Should().Be("Processing");
        }

        first.Status.Should().Be("Processing");

        // Monobank cancel fired exactly once - the second request short-circuited.
        monobankMock.Verify(
            m => m.CancelInvoiceAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<MonobankBasketItem>>(), It.IsAny<CancellationToken>()),
            Times.Once());

        using (var verify = CreateContext())
        {
            var refunds = await verify.Payments.AsNoTracking().Where(r => r.OrderId == orderId && r.Kind == PaymentKind.Refund).ToListAsync();
            refunds.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task ManualRefund_WhenPreviousRefundFailed_ShouldRetryInPlaceWithoutSecondRow()
    {
        // Regression (#53/R4): only a Failed refund is retryable, and the retry reuses the
        // same row (unique index on order_id) instead of opening a second one - Processing/
        // Completed are never re-cancelled (see ManualRefund_WhenRequestedTwice...).
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var invoiceId = $"test-invoice-{orderId:N}";
        var failedRefundId = Guid.NewGuid();

        await SeedAsync(seed =>
        {
            seed.Users.Add(CreateUser(userId));
            AddPaidOrder(seed, CreateOrder(orderId, userId, invoiceId, OrderStatus.PendingFulfillment,
                quantity: 2, unitPrice: 1000), invoiceId);

            // A prior refund attempt Monobank (or the network) rejected.
            SeedRefundPayment(seed, orderId, invoiceId, PaymentStatus.Failed, 200000, id: failedRefundId);
        });

        var monobankMock = CreateMonobankMock(out _);

        RefundOrderResult retry;
        using (var context = CreateContext())
        {
            var handler = new RefundOrderCommandHandler(context, new StubMonobankClientFactory(monobankMock.Object), new AuditEventService(context));
            retry = await handler.HandleAsync(new RefundOrderCommand { OrderId = orderId, ChangedByUserName = "admin-test" });
        }

        retry.Status.Should().Be("Processing");
        retry.RefundId.Should().Be(failedRefundId); // same row, flipped back to in-flight

        // The retry issued a fresh Monobank cancel.
        monobankMock.Verify(
            m => m.CancelInvoiceAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<MonobankBasketItem>>(), It.IsAny<CancellationToken>()),
            Times.Once());

        using (var verify = CreateContext())
        {
            var refund = await verify.Payments.AsNoTracking().SingleAsync(r => r.OrderId == orderId && r.Kind == PaymentKind.Refund);
            refund.Id.Should().Be(failedRefundId);
            refund.Status.Should().Be(PaymentStatus.Pending);
            refund.ErrorMessage.Should().BeNull();
        }
    }

    [Fact]
    public async Task ManualRefund_WhenCallerSuppliesOverAmount_ShouldCapAtUnfulfilledValue()
    {
        // Regression (#53/R6): the refund is capped at the unfulfilled value even when the
        // caller passes a larger AmountKopecks, so an oversized request can never refund
        // vouchers that were already delivered.
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var invoiceId = $"test-invoice-{orderId:N}";

        await SeedAsync(seed =>
        {
            seed.Users.Add(CreateUser(userId));

            // 3 units at 520 UAH; 1 delivered -> only 2 * 520 UAH is refundable.
            AddPaidOrder(seed, CreateOrder(orderId, userId, invoiceId, OrderStatus.PartiallyFulfilled,
                quantity: 3, unitPrice: 520), invoiceId);

            var voucher = new FuelVoucher
            {
                Id = Guid.NewGuid(),
                BrandId = BrandIds.ForSlug("okko"),
                Brand = "okko",
                FuelTypeId = ToUuid("okko-dp"),
                Liters = 10m,
                SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow).AddMonths(1),
                CustomerExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow).AddMonths(1),
                VoucherNumber = $"OC-{orderId:N}-000",
                QrPayload = $"payload-{orderId:N}-000",
                Status = VoucherStatus.Assigned,
                AssignedToUserId = userId,
                OrderId = orderId, // the order under test is the purchase that delivered it
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            };
            seed.FuelVouchers.Add(voucher);
            seed.Fulfillments.Add(new Fulfillment
            {
                OrderId = orderId,
                VoucherId = voucher.Id,
                FulfilledAtUtc = TestClock.UtcNow.AddHours(-1)
            });
        });

        var monobankMock = CreateMonobankMock(out var cancelledAmount);

        RefundOrderResult result;
        using (var context = CreateContext())
        {
            var handler = new RefundOrderCommandHandler(context, new StubMonobankClientFactory(monobankMock.Object), new AuditEventService(context));
            result = await handler.HandleAsync(new RefundOrderCommand
            {
                OrderId = orderId,
                AmountKopecks = 999_999, // absurd over-refund, far above the full order value
                ChangedByUserName = "admin-test"
            });
        }

        // Capped to the 2 undelivered units (2 * 520 UAH), not the supplied 999_999.
        result.AmountKopecks.Should().Be(104000);
        cancelledAmount.Value.Should().Be(104000);

        using (var verify = CreateContext())
        {
            var refund = await verify.Payments.AsNoTracking().SingleAsync(r => r.OrderId == orderId && r.Kind == PaymentKind.Refund);
            refund.AmountKopecks.Should().Be(104000);
        }
    }


    [Fact]
    public async Task ManualRefund_WithDeliveredVouchers_ShouldFlipOrderToPartiallyRefunded()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var invoiceId = $"test-invoice-{orderId:N}";

        await SeedAsync(seed =>
        {
            seed.Users.Add(CreateUser(userId));

            var order = CreateOrder(orderId, userId, invoiceId, OrderStatus.PartiallyFulfilled,
                quantity: 3, unitPrice: 520);
            seed.Orders.Add(order);
            seed.Payments.Add(SucceededCaptureFor(order, invoiceId));

            var voucher = new FuelVoucher
            {
                Id = Guid.NewGuid(),
                BrandId = BrandIds.ForSlug("okko"),
                Brand = "okko",
                FuelTypeId = ToUuid("okko-dp"),
                Liters = 10m,
                SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow).AddMonths(1),
                CustomerExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow).AddMonths(1),
                VoucherNumber = $"MR-{orderId:N}-000",
                QrPayload = $"payload-{orderId:N}-000",
                Status = VoucherStatus.Assigned,
                AssignedToUserId = userId,
                OrderId = orderId, // the order under test is the purchase that delivered it
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            };
            seed.FuelVouchers.Add(voucher);
            seed.Fulfillments.Add(new Fulfillment
            {
                OrderId = orderId,
                VoucherId = voucher.Id,
                FulfilledAtUtc = TestClock.UtcNow.AddHours(-1)
            });
        });

        var monobankMock = CreateMonobankMock(out _);
        monobankMock.Setup(m => m.GetInvoiceStatusAsync(invoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonobankInvoiceStatus
            {
                InvoiceId = invoiceId,
                Status = "success",
                CancelList = new List<MonobankCancelListItem>
                {
                    new()
                    {
                        Status = "success",
                        ExtRef = orderId.ToString(),
                        ModifiedDate = TestClock.UtcNow
                    }
                }
            });

        RefundOrderResult result;
        using (var context = CreateContext())
        {
            var handler = new RefundOrderCommandHandler(context, new StubMonobankClientFactory(monobankMock.Object), new AuditEventService(context));
            result = await handler.HandleAsync(new RefundOrderCommand { OrderId = orderId });
        }

        // Only the two undelivered vouchers are refundable: 2 * 520 UAH.
        result.AmountKopecks.Should().Be(104000);

        using (var syncContext = CreateContext())
        {
            var syncService = new RefundStatusSyncService(
                syncContext, new StubMonobankClientFactory(monobankMock.Object), NullLogger<RefundStatusSyncService>.Instance);
            await syncService.SyncPendingRefundsAsync();
        }

        using (var verify = CreateContext())
        {
            var refund = await verify.Payments.AsNoTracking().SingleAsync(r => r.OrderId == orderId && r.Kind == PaymentKind.Refund);
            refund.Status.Should().Be(PaymentStatus.Succeeded);
            refund.AmountKopecks.Should().Be(104000);

            var order = await verify.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
            order.Status.Should().Be(OrderStatus.PartiallyRefunded);

            // A partial refund books what came back, not what was paid. The 2026-11-02 production
            // case returned 862.00 of a 1724.00 order and the book still carried the whole 1724.00.
            var booked = await BookedRefundLegsAsync(refund.Id);
            booked.Should().HaveCount(2);
            booked.Should().Contain(("payment.refunded", "681", "311", 1040.00m));
            booked.Should().Contain(("payment.refunded.vat", "643", "704", 173.33m));
        }
    }

    [Fact]
    public async Task ManualRefund_WhenMonobankReportsFailure_ShouldFailRefundAndKeepOrderStatus()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var invoiceId = $"test-invoice-{orderId:N}";

        await SeedAsync(seed =>
        {
            seed.Users.Add(CreateUser(userId));
            AddPaidOrder(seed, CreateOrder(orderId, userId, invoiceId, OrderStatus.PendingFulfillment,
                quantity: 1, unitPrice: 1000), invoiceId);
        });

        var monobankMock = CreateMonobankMock(out _);
        monobankMock.Setup(m => m.GetInvoiceStatusAsync(invoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonobankInvoiceStatus
            {
                InvoiceId = invoiceId,
                Status = "success",
                CancelList = new List<MonobankCancelListItem>
                {
                    new()
                    {
                        Status = "failure",
                        ExtRef = orderId.ToString(),
                        ModifiedDate = TestClock.UtcNow
                    }
                }
            });

        using (var context = CreateContext())
        {
            var handler = new RefundOrderCommandHandler(context, new StubMonobankClientFactory(monobankMock.Object), new AuditEventService(context));
            await handler.HandleAsync(new RefundOrderCommand { OrderId = orderId });
        }

        using (var syncContext = CreateContext())
        {
            var syncService = new RefundStatusSyncService(
                syncContext, new StubMonobankClientFactory(monobankMock.Object), NullLogger<RefundStatusSyncService>.Instance);
            await syncService.SyncPendingRefundsAsync();
        }

        using (var verify = CreateContext())
        {
            var refund = await verify.Payments.AsNoTracking().SingleAsync(r => r.OrderId == orderId && r.Kind == PaymentKind.Refund);
            refund.Status.Should().Be(PaymentStatus.Failed);
            // Same as above: the brand word is gone, the typed Failed status is the record.
            refund.ErrorMessage.Should().Be("Monobank reported refund failure");

            // Money that never went back must not appear in the books.
            (await BookedRefundLegsAsync(refund.Id)).Should().BeEmpty();

            // A failed refund must never leave the order in a refunded state.
            var order = await verify.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
            order.Status.Should().Be(OrderStatus.PendingFulfillment);
        }
    }

    [Fact]
    public async Task StaleRefund_OlderThan24h_ShouldTimeOutAndRevertPrematureOrderStatus()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await SeedAsync(seed =>
        {
            seed.Users.Add(CreateUser(userId));

            // Legacy inconsistent state: order already shows refunded while its
            // refund is still Processing (and has been for over a day).
var order = CreateOrder(orderId, userId, $"test-invoice-{orderId:N}", OrderStatus.PartiallyRefunded,
                quantity: 2, unitPrice: 1000);
            AddPaidOrder(seed, order, $"test-invoice-{orderId:N}");

            var voucher = new FuelVoucher
            {
                Id = Guid.NewGuid(),
                BrandId = BrandIds.ForSlug("okko"),
                Brand = "okko",
                FuelTypeId = ToUuid("okko-dp"),
                Liters = 10m,
                SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow).AddMonths(1),
                CustomerExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow).AddMonths(1),
                VoucherNumber = $"SR-{orderId:N}-000",
                QrPayload = $"payload-{orderId:N}-000",
                Status = VoucherStatus.Assigned,
                AssignedToUserId = userId,
                OrderId = orderId, // the order under test is the purchase that delivered it
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            };
            seed.FuelVouchers.Add(voucher);
            seed.Fulfillments.Add(new Fulfillment
            {
                OrderId = orderId,
                VoucherId = voucher.Id,
                FulfilledAtUtc = TestClock.UtcNow.AddDays(-2)
            });

            // 25h old, so the sync service's stale-refund sweep picks it up.
            SeedRefundPayment(seed, orderId, order.MonobankInvoiceId!, PaymentStatus.Pending, 100000,
                TimeSpan.FromHours(25));
        });

        var monobankMock = CreateMonobankMock(out _);

        using (var syncContext = CreateContext())
        {
            var syncService = new RefundStatusSyncService(
                syncContext, new StubMonobankClientFactory(monobankMock.Object), NullLogger<RefundStatusSyncService>.Instance);
            await syncService.SyncPendingRefundsAsync();
        }

        using (var verify = CreateContext())
        {
            var refund = await verify.Payments.AsNoTracking().SingleAsync(r => r.OrderId == orderId && r.Kind == PaymentKind.Refund);
            refund.Status.Should().Be(PaymentStatus.Failed);
            refund.ErrorMessage.Should().Contain("Timed out");

            var order = await verify.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
            order.Status.Should().Be(OrderStatus.PartiallyFulfilled);
        }
    }

    private static User CreateUser(Guid userId) => new()
    {
        Id = userId,
        PhoneNumber = $"+38{userId:N}"[..20],
        IsActive = true,
        CreatedAtUtc = TestClock.UtcNow,
        UpdatedAtUtc = TestClock.UtcNow
    };

    /// <summary>
    /// The succeeded capture every paid order has.
    /// </summary>
/// <remarks>
/// Both checkout paths add the order and its capture in one <c>SaveChangesAsync</c>, so an order
/// carrying a Monobank invoice but no capture row is a state the system cannot reach. It used to be
/// reachable here: these fixtures seeded a paid order directly and skipped the capture, and a refund
/// only needed the order's own <c>MonobankStatus</c> to be told to go ahead. A refund now reverses a
/// specific capture, which is the guarantee that makes it traceable, and the handler refuses an order
/// that has none - so the fixture seeds what the database would really hold.
/// </remarks>
private static Payment SucceededCaptureFor(Order order, string invoiceId) => new()
    {
        Id = Guid.CreateVersion7(),
        OrderId = order.Id,
        Kind = PaymentKind.Capture,
        Provider = WebhookEventIdentity.Monobank,
        Merchant = MonobankMerchant.Live,
        ProviderInvoiceId = invoiceId,
        AmountUah = order.Price,
        AmountKopecks = Money.ToKopecks(order.Price),
        Currency = "UAH",
        Status = PaymentStatus.Succeeded,
        PaidAtUtc = order.CreatedAtUtc,
        CreatedAtUtc = order.CreatedAtUtc,
        UpdatedAtUtc = order.CreatedAtUtc
    };

/// <summary>Seeds a paid order together with the capture it was paid by.</summary>
private static Order AddPaidOrder(
        ApplicationDbContext seed,
        Order order,
        string invoiceId)
    {
        seed.Orders.Add(order);
        seed.Payments.Add(SucceededCaptureFor(order, invoiceId));
        return order;
    }

    private static Order CreateOrder(
        Guid orderId,
        Guid userId,
        string invoiceId,
        OrderStatus status,
        int quantity,
        int unitPrice) => new()
    {
        Id = orderId,
        UserId = userId,
        Price = quantity * unitPrice,
        Status = status,
        MonobankInvoiceId = invoiceId,
        MonobankStatus = MonobankStatus.Success,
        CreatedAtUtc = TestClock.UtcNow.AddDays(-1),
        UpdatedAtUtc = TestClock.UtcNow.AddDays(-1),
        LineItems = new List<OrderLineItem>
        {
            new()
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                Brand = "okko",
                BrandId = BrandIds.ForSlug("okko"),
                FuelTypeId = ToUuid("okko-dp"),
                Liters = 10m,
                Quantity = quantity,
                UnitPrice = unitPrice,
                LineTotal = quantity * unitPrice
            }
        }
    };

    /// <summary>
    /// What the journal booked for one refund, as (event, debit account, credit account, amount).
    /// Read against the real seeded chart on purpose: the point of the entry is which accounts it
    /// names, and a purpose-built chart would prove the engine works and nothing about the books.
    /// </summary>
    private async Task<List<(string SourceEvent, string Debit, string Credit, decimal Amount)>> BookedRefundLegsAsync(
        Guid refundPaymentId)
    {
        using var ctx = CreateContext();
        var entries = await ctx.JournalEntries
            .AsNoTracking()
            .Include(e => e.Lines)
            .ThenInclude(l => l.Account)
            .Where(e => e.SourceType == JournalSourceType.Refund && e.SourceId == refundPaymentId)
            .ToListAsync();

        return entries
            .Select(e => (
                e.SourceEvent,
                e.Lines.Single(l => l.DebitUah > 0m).Account!.Code,
                e.Lines.Single(l => l.CreditUah > 0m).Account!.Code,
                e.Lines.Sum(l => l.DebitUah)))
            .ToList();
    }

    /// <summary>Seeds a refund payment against the capture <see cref="AddPaidOrder"/> put there.</summary>
/// <remarks>
/// A refund names the capture it reverses, so this reads the capture back out of the change
/// tracker rather than minting a parent - <c>AddPaidOrder</c> has already added it and it is not
/// in the database yet.
/// </remarks>
private static Payment SeedRefundPayment(
        ApplicationDbContext seed,
        Guid orderId,
        string invoiceId,
        PaymentStatus status,
        int amountKopecks,
        TimeSpan? age = null,
        Guid? id = null)
    {
        var capture = seed.Payments.Local.First(p => p.OrderId == orderId && p.Kind == PaymentKind.Capture);

        var refund = new Payment
        {
            Id = id ?? Guid.CreateVersion7(),
            OrderId = orderId,
            Kind = PaymentKind.Refund,
            ParentPaymentId = capture.Id,
            Provider = WebhookEventIdentity.Monobank,
            ProviderInvoiceId = invoiceId,
            AmountUah = amountKopecks / 100m,
            AmountKopecks = amountKopecks,
            Currency = "UAH",
            Status = status,
            ErrorMessage = status == PaymentStatus.Failed ? "Monobank reported refund failure" : null,
            // A failed refund reads as a past attempt; the sync service's stale sweep only ever looks
            // BACK from a cutoff, so a refund dated in the future would sit there forever.
            CreatedAtUtc = TestClock.UtcNow - (age ?? TimeSpan.FromMinutes(5)),
            UpdatedAtUtc = TestClock.UtcNow - (age ?? TimeSpan.FromMinutes(5))
        };

        seed.Payments.Add(refund);
        return refund;
    }

    private static Mock<IMonobankClient> CreateMonobankMock(out AmountBox cancelledAmount)
    {
        var captured = new AmountBox();
        var mock = new Mock<IMonobankClient>();
        mock.Setup(m => m.CancelInvoiceAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<MonobankBasketItem>>(), It.IsAny<CancellationToken>()))
            .Callback<string, long, string, IReadOnlyList<MonobankBasketItem>, CancellationToken>((_, amount, _, _, _) => captured.Value = amount)
            .ReturnsAsync(new MonobankCancelResponse { Status = "processing" });
        cancelledAmount = captured;
        return mock;
    }

    /// <summary>Reference holder so the captured cancel amount outlives the mock setup.</summary>
    private sealed class AmountBox
    {
        public long Value { get; set; }
    }

    private async Task SeedAsync(Action<ApplicationDbContext> seed)
    {
        using var context = CreateContext();
        await context.Database.MigrateAsync();

        // The sync service scans ALL refunds, so leftover state from a previous test in
        // this shared class-fixture database would interfere. Keep each test hermetic.
        await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "payments", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");

        seed(context);
        await context.SaveChangesAsync();

        // These fixtures fabricate the paid capture row instead of driving the settle. The refund
        // mirrors the receipt, and its VAT leg is capped by the carve that receipt booked
        // (CarvedVat) - so the receipt the settle would have written is written here, or a refund
        // would be asking to give back VAT no entry ever asserted.
        foreach (var order in context.Orders.ToList())
        {
            if (order.Price <= 0m)
                continue;

            var journal = TestJournal.For(context);
            var (netUah, vatUah) = VatCalculator.Split(order.Price, VatCalculator.StandardRate);
            var entryDate = DateOnly.FromDateTime(order.CreatedAtUtc);

            await journal.PostAsync(new PostJournalEntryRequest
            {
                SourceEvent = "payment.received",
                SourceType = JournalSourceType.Payment,
                SourceId = order.Id,
                EntryDate = entryDate,
                AmountUah = netUah + vatUah,
                Description = $"Оплата замовлення {order.Id}",
                PostedByUserId = order.UserId,
            });
            await journal.PostAsync(new PostJournalEntryRequest
            {
                SourceEvent = "payment.received.vat",
                SourceType = JournalSourceType.Payment,
                SourceId = order.Id,
                EntryDate = entryDate,
                AmountUah = vatUah,
                Description = $"ПДВ з оплати замовлення {order.Id}",
                PostedByUserId = order.UserId,
            });
        }
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);
}

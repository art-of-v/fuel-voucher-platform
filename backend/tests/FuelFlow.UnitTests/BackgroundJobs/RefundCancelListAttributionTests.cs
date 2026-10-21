using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.SharedKernel.Domain;
using Microsoft.Extensions.Logging;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace FuelFlow.UnitTests.BackgroundJobs;

/// <summary>
/// #261: the sync used to fall back to "the newest entry in the cancel list, whatever it refers
/// to". With two partial refunds on one invoice that credits the wrong one — its entry says
/// "success", so this refund is marked Completed and the order is moved to PartiallyRefunded on
/// the strength of a confirmation that belonged to a different refund. Silent, and it is money.
/// </summary>
public sealed class RefundCancelListAttributionTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly RefundStatusSyncService _service;

    public RefundCancelListAttributionTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"cancellist-{Guid.NewGuid():N}")
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);

        _service = new RefundStatusSyncService(
            _context,
            Mock.Of<IMonobankClientFactory>(),
            NullLogger<RefundStatusSyncService>.Instance);
    }

    private const string InvoiceId = "inv-two-refunds";

    /// <summary>
    /// Two pending refunds on one invoice, each with its own acquirer reference.
    /// </summary>
    /// <remarks>
    /// The reference is the order's idempotency key now rather than a copy kept on the refund, so
    /// telling two refunds apart needs two orders - and <c>ux_payments_one_refund_per_order</c> means
    /// the database no longer permits two refunds on one order at all. Seeding the shape the schema
    /// forbids keeps this honest: the sibling guard is now defence in depth, and
    /// <see cref="An_Entry_Belonging_To_Another_Refund_Does_Not_Complete_This_One"/> is what that
    /// guard buys if it is ever reached.
    /// </remarks>
    private async Task<(Guid FirstId, Guid SecondId)> SeedTwoRefundsAsync()
    {
        var (_, firstId) = await SeedRefundAsync("ext-first");
        var (_, secondId) = await SeedRefundAsync("ext-second");
        return (firstId, secondId);
    }

    private async Task<(Guid OrderId, Guid RefundId)> SeedRefundAsync(string extRef)
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        _context.Users.Add(new User
        {
            Id = userId,
            PhoneNumber = $"+38{userId:N}"[..20],
            IsActive = true,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        _context.Orders.Add(new Order
        {
            Id = orderId,
            UserId = userId,
            Price = 100m,
            Status = OrderStatus.PartiallyFulfilled,
            IdempotencyKey = extRef,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        var capture = new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            Kind = PaymentKind.Capture,
            Provider = "monobank",
            ProviderInvoiceId = InvoiceId,
            AmountUah = 500.00m,
            AmountKopecks = 50000,
            Currency = "UAH",
            Status = PaymentStatus.Succeeded,
            PaidAtUtc = TestClock.UtcNow,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        };
        _context.Payments.Add(capture);

        var refund = new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            Kind = PaymentKind.Refund,
            ParentPaymentId = capture.Id,
            Provider = "monobank",
            ProviderInvoiceId = InvoiceId,
            AmountUah = 500.00m,
            AmountKopecks = 50000,
            Currency = "UAH",
            Status = PaymentStatus.Pending,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        };
        _context.Payments.Add(refund);

        await _context.SaveChangesAsync();
        return (orderId, refund.Id);
    }

    private static MonobankCancelListItem Entry(
        string? extRef, string status, int minutesAgo) => new()
    {
        Status = status,
        ExtRef = extRef,
        ModifiedDate = TestClock.UtcNow.AddMinutes(-minutesAgo),
    };

    [Fact]
    public async Task An_Entry_Belonging_To_Another_Refund_Does_Not_Complete_This_One()
    {
        var (firstId, secondId) = await SeedTwoRefundsAsync();

        // The bank answered about the first refund only, and our second refund's extRef is not here.
        var cancelList = new List<MonobankCancelListItem>
        {
            Entry("ext-first", "success", minutesAgo: 5),
        };

        await _service.SyncRefundForInvoiceAsync(InvoiceId, cancelList);

        var after = await _context.Payments.AsNoTracking().Where(r => r.Kind == PaymentKind.Refund).ToListAsync();

        after.Single(r => r.Id == firstId).Status
            .Should().Be(PaymentStatus.Succeeded, "its own extRef matched");

        after.Single(r => r.Id == secondId).Status
            .Should().Be(PaymentStatus.Pending,
                "the only entry belongs to the other refund, and crediting it would move a real order to a refunded state on someone else's confirmation");

        // No assertion on the order here on purpose: the first refund legitimately matched and
        // ApplyConfirmedRefundToOrderAsync did its job, so the order moving is correct. What must not
        // happen is the SECOND refund being completed on the first refund's confirmation.
        after.Single(r => r.Id == secondId).PaidAtUtc.Should().BeNull(
            "nothing was ever written about this refund's status at Monobank");
    }

    [Fact]
    public async Task A_Lone_Refund_May_Still_Take_The_Lone_Entry()
    {
        var (_, refundId) = await SeedRefundAsync("ext-only");

        // The reason the fallback exists at all: the bank echoes something other than our extRef.
        // One refund, one entry - there is nothing else it could be referring to.
        var cancelList = new List<MonobankCancelListItem>
        {
            Entry(extRef: null, "success", minutesAgo: 1),
        };

        await _service.SyncRefundForInvoiceAsync(InvoiceId, cancelList);

        var after = await _context.Payments.AsNoTracking().SingleAsync(r => r.Id == refundId);
        after.Status.Should().Be(PaymentStatus.Succeeded,
            "a single entry with a single pending refund cannot be somebody else's");
    }

    [Fact]
    public async Task An_Ambiguous_List_Leaves_Every_Refund_Alone()
    {
        var (firstId, secondId) = await SeedTwoRefundsAsync();

        // Two entries, neither extRef ours, and two refunds waiting: nothing here is attributable.
        var cancelList = new List<MonobankCancelListItem>
        {
            Entry("somebody-elses-ref", "success", minutesAgo: 1),
            Entry("another-ref", "success", minutesAgo: 2),
        };

        await _service.SyncRefundForInvoiceAsync(InvoiceId, cancelList);

        var after = await _context.Payments.AsNoTracking().Where(r => r.Kind == PaymentKind.Refund).ToListAsync();
        after.Should().OnlyContain(r => r.Status == PaymentStatus.Pending,
            "with two entries and two refunds, any attribution is a coin flip, and the retry costs nothing");
        after.Select(r => r.Id).Should().Contain(new[] { firstId, secondId });
    }

    public void Dispose() => _context.Dispose();

    private sealed class NullLogger<T> : ILogger<T>
    {
        public static readonly NullLogger<T> Instance = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) { }
    }
}

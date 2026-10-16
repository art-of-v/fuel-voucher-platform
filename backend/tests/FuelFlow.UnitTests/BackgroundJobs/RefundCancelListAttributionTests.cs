using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.SharedKernel.Domain;
using Microsoft.Extensions.Logging;
using FuelFlow.Persistence;
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

        _context = new ApplicationDbContext(options);

        _service = new RefundStatusSyncService(
            _context,
            Mock.Of<IMonobankClientFactory>(),
            NullLogger<RefundStatusSyncService>.Instance);
    }

    private const string InvoiceId = "inv-two-refunds";

    /// <summary>Two Processing refunds on one invoice, each with its own extRef, as Monobank echoes them back.</summary>
    private async Task<(Guid FirstId, Guid SecondId)> SeedTwoRefundsAsync()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        _context.Users.Add(new User
        {
            Id = userId,
            PhoneNumber = $"+38{userId:N}"[..20],
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        _context.Orders.Add(new Order
        {
            Id = orderId,
            UserId = userId,
            Price = 100m,
            Status = OrderStatus.PartiallyFulfilled,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        var first = NewRefund(orderId, userId, "ext-first");
        var second = NewRefund(orderId, userId, "ext-second");
        _context.Refunds.AddRange(first, second);
        await _context.SaveChangesAsync();

        return (first.Id, second.Id);
    }

    private static Refund NewRefund(Guid orderId, Guid userId, string extRef) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        UserId = userId,
        AmountKopecks = 500,
        InvoiceId = InvoiceId,
        ExtRef = extRef,
        Status = RefundStatus.Processing,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
    };

    private static MonobankCancelListItem Entry(
        string? extRef, string status, int minutesAgo) => new()
    {
        Status = status,
        ExtRef = extRef,
        ModifiedDate = DateTime.UtcNow.AddMinutes(-minutesAgo),
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

        var after = await _context.Refunds.AsNoTracking().ToListAsync();

        after.Single(r => r.Id == firstId).Status
            .Should().Be(RefundStatus.Completed, "its own extRef matched");

        after.Single(r => r.Id == secondId).Status
            .Should().Be(RefundStatus.Processing,
                "the only entry belongs to the other refund, and crediting it would move a real order to a refunded state on someone else's confirmation");

        // No assertion on the order here on purpose: the first refund legitimately matched and
        // ApplyConfirmedRefundToOrderAsync did its job, so the order moving is correct. What must not
        // happen is the SECOND refund being completed on the first refund's confirmation.
        after.Single(r => r.Id == secondId).MonobankStatus.Should().BeNull(
            "nothing was ever written about this refund's status at Monobank");
    }

    [Fact]
    public async Task A_Lone_Refund_May_Still_Take_The_Lone_Entry()
    {
        var orderId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var refund = NewRefund(orderId, userId, "ext-only");

        _context.Users.Add(new User
        {
            Id = userId,
            PhoneNumber = $"+38{userId:N}"[..20],
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });
        _context.Orders.Add(new Order
        {
            Id = orderId,
            UserId = userId,
            Price = 100m,
            Status = OrderStatus.Fulfilled,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });
        _context.Refunds.Add(refund);
        await _context.SaveChangesAsync();

        // The reason the fallback exists at all: the bank echoes something other than our extRef.
        // One refund, one entry - there is nothing else it could be referring to.
        var cancelList = new List<MonobankCancelListItem>
        {
            Entry(extRef: null, "success", minutesAgo: 1),
        };

        await _service.SyncRefundForInvoiceAsync(InvoiceId, cancelList);

        var after = await _context.Refunds.AsNoTracking().SingleAsync(r => r.Id == refund.Id);
        after.Status.Should().Be(RefundStatus.Completed,
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

        var after = await _context.Refunds.AsNoTracking().ToListAsync();
        after.Should().OnlyContain(r => r.Status == RefundStatus.Processing,
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

using FluentAssertions;
using FuelFlow.Features.Orders.SettlePaidOrder;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.UnitTests.Payments;

/// <summary>
/// The step that pays for an order out of the money the customer already holds, asked about an order
/// that owes nothing.
/// </summary>
/// <remarks>
/// A balance can only answer for an amount, and a free order asks for none. Its price is 0, so the
/// money on it (also 0) already covers it, and the rule "covered means settle" would hand it to the
/// core - which books the receipt as the order's price and is refused by the journal, because a
/// double-entry leg of 0 ₴ is not an entry ("Amount for 'payment.received' must be greater than
/// zero"). Before that gate the checkout whose discount clamps the price at zero (planning #787's
/// generous term tier) threw a 500 instead of writing the order, which is what
/// <see cref="Orders.VoucherTermSaleTests.DiscountLargerThanThePrice_ClampsAtZeroRatherThanPayingTheCustomer"/>
/// caught on CI.
/// </remarks>
public sealed class HeldMoneyAndFreeOrderTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public HeldMoneyAndFreeOrderTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task A_Free_Order_Is_Answered_Not_Settled_Because_Its_Receipt_Would_Book_Nothing()
    {
        var order = await SeedOrderAsync(price: 0m);
        // Enough money to "cover" a free order, which is exactly the trap: it is the customer's money
        // for the sale they have not made yet, and a draw on a zero price would take it out of the
        // balance for nothing.
        await SeedHeldPaymentAsync(order.UserId, 500m);

        var result = await TestJournal.HeldMoneyFor(_context)
            .HandleAsync(new SettleFromHeldMoneyCommand { OrderId = order.Id, Because = "the test" });

        result.Settled.Should().BeFalse("a free order is not a sale the balance pays for");
        result.Outcome.Should().Be(HeldMoneyOutcome.NothingHeld);

        (await _context.PaymentAllocations.CountAsync()).Should().Be(0,
            "nothing was drawn, so the balance is exactly what it was");
        (await _context.JournalEntries.CountAsync()).Should().Be(0,
            "the step never reaches the books, which is the whole point of the gate");

        var saved = await _context.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
        saved.Status.Should().Be(OrderStatus.PendingPayment,
            "the order stays on the route its creation chose, as it did before balances existed");
    }

    private async Task<Order> SeedOrderAsync(decimal price)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Price = price,
            Status = OrderStatus.PendingPayment,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        };

        _context.Users.Add(new User
        {
            Id = order.UserId,
            PhoneNumber = "+380110010203",
            IsActive = true,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        return order;
    }

    private async Task SeedHeldPaymentAsync(Guid userId, decimal amount)
    {
        _context.Payments.Add(new Payment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OrderId = null,
            Kind = PaymentKind.Capture,
            Provider = PaymentProviders.Declared,
            ProviderInvoiceId = $"DECL-{Guid.NewGuid():N}",
            AmountUah = amount,
            AmountKopecks = (long)Math.Round(amount * 100m),
            Currency = "UAH",
            Status = PaymentStatus.Succeeded,
            PaidAtUtc = TestClock.UtcNow,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        await _context.SaveChangesAsync();
    }
}

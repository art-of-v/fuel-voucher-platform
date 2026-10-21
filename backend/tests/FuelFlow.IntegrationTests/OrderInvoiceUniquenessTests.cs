using FluentAssertions;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// <c>orders.monobank_invoice_id</c> had no index at all, so the webhook that resolves the order on
/// the anonymous payment path was doing a sequential scan of <c>orders</c> per delivery. It also had
/// no uniqueness, and it is read with <c>FirstOrDefaultAsync</c> - so two orders sharing an invoice
/// would let a payment fulfil whichever row the plan happened to return, silently.
/// </summary>
[Collection("Integration Tests")]
public sealed class OrderInvoiceUniquenessTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public OrderInvoiceUniquenessTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    [Fact]
    public async Task TwoOrders_CannotClaimTheSameInvoiceId()
    {
        var userId = await SeedUserAsync();
        var invoice = $"INV-UNIQ-{Guid.NewGuid():N}"[..24];

        await using (var seed = CreateContext())
        {
            seed.Orders.Add(NewOrder(userId, invoice));
            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext();
        context.Orders.Add(NewOrder(userId, invoice));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>(
            "the webhook reads this column with FirstOrDefault, so a duplicate would fulfil the wrong order");
    }

    /// <summary>
    /// Partial, so the many orders that have not reached the payment provider yet - and the ones
    /// whose invoice was never issued - do not collide with each other. Without this the index would
    /// be unusable on day one.
    /// </summary>
    [Fact]
    public async Task ManyOrders_WithNoInvoiceId_Coexist()
    {
        var userId = await SeedUserAsync();

        await using var context = CreateContext();
        for (var i = 0; i < 5; i++)
            context.Orders.Add(NewOrder(userId, invoiceId: null));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().NotThrowAsync("a null invoice is not a duplicate invoice");
    }

    [Fact]
    public async Task DifferentOrders_CanShareAnInvoiceOnlyWhenItIsActuallyDifferent()
    {
        var userId = await SeedUserAsync();

        await using var context = CreateContext();
        context.Orders.Add(NewOrder(userId, $"INV-A-{Guid.NewGuid():N}"[..24]));
        context.Orders.Add(NewOrder(userId, $"INV-B-{Guid.NewGuid():N}"[..24]));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().NotThrowAsync();
    }

    private static Order NewOrder(Guid userId, string? invoiceId) => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = userId,
        Price = 100m,
        Status = OrderStatus.PendingPayment,
        Kind = OrderKind.Purchase,
        MonobankInvoiceId = invoiceId,
        CreatedAtUtc = TestClock.UtcNow,
        UpdatedAtUtc = TestClock.UtcNow
    };

    private async Task<Guid> SeedUserAsync()
    {
        var id = Guid.CreateVersion7();
        await using var context = CreateContext();
        context.Users.Add(new User
        {
            Id = id,
            PhoneNumber = $"+38051{TestClock.UniqueDigits(7)}",
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });
        await context.SaveChangesAsync();
        return id;
    }
}
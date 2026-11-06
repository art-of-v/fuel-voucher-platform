using FluentAssertions;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace FuelFlow.IntegrationTests.Payments;

/// <summary>
/// The database rules that make a declared transfer a balance rather than a voucher for one sale.
/// </summary>
/// <remarks>
/// <para>
/// A balance is an arithmetic invariant — the money drawn out of a transfer never exceeds the money
/// in it — and an invariant that only exists inside one process's arithmetic is not an invariant. The
/// checkout reads what is free, plans a draw against it, and writes it; two of those running at once
/// over one 1 000,00 ₴ transfer both plan legally. The handler cannot see the other request, so the
/// guard is a trigger that locks the payment row before it sums, which turns the race into a refusal
/// for the loser instead of a sale paid for with money that was already spent.
/// </para>
/// <para>
/// These tests write through raw SQL and a psql-shaped context, not through the handler, because the
/// claim is about the table: an operator correcting a draw by hand meets the same wall as the code
/// does. What the handler does with the refusal — the restated shortfall rather than a 500 — is
/// <see cref="FuelFlow.IntegrationTests.Orders.HeldMoneyPaysForTheCheckoutTests"/>.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class HeldMoneyDrawGuardTests : IClassFixture<TestDatabaseFixture>, IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;

    public HeldMoneyDrawGuardTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        using var context = CreateContext();
        await PaymentTestReset.RunAsync(context);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── The sum: what a transfer holds is what it held, minus what has been drawn ────────────────

    [Fact]
    public async Task Draws_Until_The_Transfer_Is_Empty_Are_Accepted()
    {
        var payment = await SeedPaymentAsync(1_000m);
        var first = await SeedOrderAsync();
        var second = await SeedOrderAsync();

        await using var context = CreateContext();
        (await ErrorOfAsync(() => DrawAsync(context, payment, first, 600m))).Should().BeNull();
        (await ErrorOfAsync(() => DrawAsync(context, payment, second, 400m))).Should().BeNull(
            "1 000,00 ₴ asked for out of 1 000,00 ₴ is the whole balance, and the whole balance may be spent");
    }

    [Fact]
    public async Task Drawing_More_Than_The_Transfer_Holds_Is_Refused()
    {
        var payment = await SeedPaymentAsync(1_000m);
        var order = await SeedOrderAsync();

        await using var context = CreateContext();
        var error = await ErrorOfAsync(() => DrawAsync(context, payment, order, 1_000.01m));

        error.Should().NotBeNull("a draw is a promise that the money is there, and this one is not")
            .And.Contain("1000.00");
    }

    [Fact]
    public async Task Drawing_The_Same_Money_Twice_Is_Refused_The_Second_Time()
    {
        var payment = await SeedPaymentAsync(1_000m);
        var first = await SeedOrderAsync();
        var second = await SeedOrderAsync();

        await using var context = CreateContext();
        await DrawAsync(context, payment, first, 1_000m);

        var error = await ErrorOfAsync(() => DrawAsync(context, payment, second, 1_000m));

        error.Should().NotBeNull("the same banknote cannot pay for two sales, whatever each request read")
            .And.Contain("already drawn on");
    }

    [Fact]
    public async Task A_Draw_Of_Nothing_Or_Less_Is_Refused()
    {
        var payment = await SeedPaymentAsync(1_000m);
        var order = await SeedOrderAsync();

        await using var context = CreateContext();

        (await ErrorOfAsync(() => DrawAsync(context, payment, order, 0m)))
            .Should().NotBeNull("a zero row would let the balance look allocated without spending any of it");
        (await ErrorOfAsync(() => DrawAsync(context, payment, order, -5m)))
            .Should().NotBeNull("a negative draw is how a refund smuggles itself into the money that pays for sales");
    }

    // ── What a draw may be drawn against ────────────────────────────────────────────────────────

    [Fact]
    public async Task Drawing_On_Money_Already_Attached_To_An_Order_Is_Refused()
    {
        var attachedTo = await SeedOrderAsync();
        var payment = await SeedPaymentAsync(1_000m, orderId: attachedTo);
        var order = await SeedOrderAsync();

        await using var context = CreateContext();
        var error = await ErrorOfAsync(() => DrawAsync(context, payment, order, 100m));

        error.Should().NotBeNull(
            "a payment naming an order is counted on that order in full, so a draw against it is the same "
            + "money twice and an order settled on money the company never received")
            .And.Contain("counted on it in full");
    }

    [Fact]
    public async Task Drawing_On_Money_That_Has_Not_Arrived_Is_Refused()
    {
        var payment = await SeedPaymentAsync(1_000m, status: PaymentStatus.Pending);
        var order = await SeedOrderAsync();

        await using var context = CreateContext();
        var error = await ErrorOfAsync(() => DrawAsync(context, payment, order, 100m));

        error.Should().NotBeNull("an invoice nobody has paid is not money in an account")
            .And.Contain("nobody holds that money yet");
    }

    [Fact]
    public async Task Drawing_On_A_Refund_Is_Refused()
    {
        var capture = await SeedPaymentAsync(1_000m);
        var refund = await SeedPaymentAsync(400m, kind: PaymentKind.Refund, parentPaymentId: capture);
        var order = await SeedOrderAsync();

        await using var context = CreateContext();
        var error = await ErrorOfAsync(() => DrawAsync(context, refund, order, 100m));

        error.Should().NotBeNull("a refund is money leaving, and drawing on it would settle a sale twice "
            + "on one banknote — once when it arrived and once on its way out")
            .And.Contain("money going back out is not a balance to draw on");
    }

    [Fact]
    public async Task Drawing_On_A_Transfer_Nobody_Is_Named_For_Is_Accepted()
    {
        // Unallocated money: declared, real, no customer yet. The balance question is a different one
        // from the money question, and this row proves the guard does not quietly add a "and it must
        // belong to someone" rule the panel's own list does not carry.
        var payment = await SeedPaymentAsync(1_000m, forCustomer: false);
        var order = await SeedOrderAsync();

        await using var context = CreateContext();

        (await ErrorOfAsync(() => DrawAsync(context, payment, order, 100m))).Should().BeNull();
    }

    // ── A draw is written, not maintained ───────────────────────────────────────────────────────

    [Fact]
    public async Task A_Draw_Cannot_Be_Changed()
    {
        var payment = await SeedPaymentAsync(1_000m);
        var order = await SeedOrderAsync();

        await using var context = CreateContext();
        await DrawAsync(context, payment, order, 300m);

        var error = await ErrorOfAsync(() => context.Database.ExecuteSqlRawAsync(
            """
            UPDATE payment_allocations SET amount_uah = 900 WHERE order_id = {0}
            """, order));

        error.Should().NotBeNull(
            "the transfer's sum is what makes it a balance, and an edit re-runs that sum against money "
            + "another order may already have spent")
            .And.Contain("take the draw back and draw again");
    }

    [Fact]
    public async Task A_Draw_Can_Be_Taken_Back()
    {
        var payment = await SeedPaymentAsync(1_000m);
        var order = await SeedOrderAsync();

        await using var context = CreateContext();
        await DrawAsync(context, payment, order, 300m);

        // The refused settle backs its rows out inside the transaction that wrote them, and a guard
        // that blocked every delete would turn that rollback into a 500. Deleting the draw is also the
        // only way an operator can undo one, so the delete is allowed and what it frees is re-summed
        // by the same arithmetic that refused the over-draw.
        (await ErrorOfAsync(() => context.Database.ExecuteSqlRawAsync(
            "DELETE FROM payment_allocations WHERE order_id = {0}", order))).Should().BeNull();

        (await ErrorOfAsync(() => DrawAsync(context, payment, order, 1_000m))).Should().BeNull(
            "the money is free again, and drawing all of it now is arithmetically honest");
    }

    [Fact]
    public async Task Money_Spent_On_Orders_Cannot_Be_Attached_To_One()
    {
        var payment = await SeedPaymentAsync(1_000m);
        var drawn = await SeedOrderAsync();
        var attaching = await SeedOrderAsync();

        await using var context = CreateContext();
        await DrawAsync(context, payment, drawn, 250m);

        var error = await ErrorOfAsync(() => context.Database.ExecuteSqlRawAsync(
            "UPDATE payments SET order_id = {1}, updated_at_utc = now() WHERE id = {0}", payment, attaching));

        error.Should().NotBeNull(
            "attaching a payment tells the money gate to count it on one order in full, which would move "
            + "the 250,00 ₴ another order already paid for into a second sale")
            .And.Contain("cannot be attached");
    }

    [Fact]
    public async Task A_Nameless_Payment_Can_Still_Be_Attached()
    {
        // The guard fires on UPDATE OF order_id and returns the row when nothing is drawn and when the
        // column is not what changed. A payment's status sync touches the row every day; a trigger that
        // could not tell "attached" from "updated" would freeze the money table.
        var payment = await SeedPaymentAsync(1_000m);
        var order = await SeedOrderAsync();

        await using var context = CreateContext();
        await DrawAsync(context, payment, order, 250m);

        (await ErrorOfAsync(() => context.Database.ExecuteSqlRawAsync(
            "UPDATE payments SET error_message = 'checked', updated_at_utc = now() WHERE id = {0}", payment)))
            .Should().BeNull("nothing here names an order, so the disjointness the trigger protects is untouched");
    }

    // ── The race the whole design leans on ──────────────────────────────────────────────────────

    /// <summary>
    /// Two checkouts, one transfer, both planning against the same read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The second request does not fail because it was written worse — it read the balance a moment
    /// earlier and its plan was legal then. It fails because the trigger takes <c>FOR UPDATE</c> on the
    /// payment before it sums, so the loser waits for the winner to commit and then sums against the
    /// money that is really left. Without the lock both sides read 1 000,00 ₴ free, both insert, and
    /// the account says it holds what it handed over twice.
    /// </para>
    /// <para>
    /// Committed-first rather than truly concurrent: a waiting transaction in a test is a hang, and the
    /// lock's job is to make the wait end at a correct sum. What is proven here is that sum.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task One_Transfer_Cannot_Be_Spent_By_Two_Requests_At_Once()
    {
        var payment = await SeedPaymentAsync(1_000m);
        var firstOrder = await SeedOrderAsync();
        var secondOrder = await SeedOrderAsync();

        await using var winner = CreateContext();
        await using var loser = CreateContext();

        await using var winnerTx = await winner.Database.BeginTransactionAsync();
        await DrawAsync(winner, payment, firstOrder, 1_000m);
        await winnerTx.CommitAsync();

        await using var loserTx = await loser.Database.BeginTransactionAsync();
        var error = await ErrorOfAsync(() => DrawAsync(loser, payment, secondOrder, 1_000m));

        error.Should().NotBeNull("the plan was legal against a balance that no longer exists")
            .And.Contain("already drawn on");

        await loserTx.RollbackAsync();

        (await winner.PaymentAllocations.AsNoTracking().CountAsync(a => a.PaymentId == payment))
            .Should().Be(1, "one transfer, one spend of it");
    }

    // ── Scaffold ────────────────────────────────────────────────────────────────────────────────

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    /// <summary>Runs a statement and hands back what PostgreSQL said, rather than throwing.</summary>
    private static async Task<string?> ErrorOfAsync(Func<Task> body)
    {
        try { await body(); return null; }
        catch (PostgresException ex) { return ex.MessageText; }
    }

    private static Task DrawAsync(
        ApplicationDbContext context, Guid paymentId, Guid orderId, decimal amount)
        => context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO payment_allocations (id, payment_id, order_id, amount_uah, created_at_utc)
            VALUES ({0}, {1}, {2}, {3}, now())
            """,
            Guid.NewGuid(), paymentId, orderId, amount);

    private async Task<Guid> SeedUserAsync()
    {
        await using var context = CreateContext();
        var userId = Guid.NewGuid();

        // settlement_code is left out and the database's own trigger assigns it: the column is unique,
        // so a seeded customer cannot share an empty one, and this file never reads the value.
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO users (id, phone_number, is_active, created_at_utc, updated_at_utc)
            VALUES ({0}, {1}, true, now(), now())
            """,
            userId, $"+38099{Guid.NewGuid().ToString("N")[..7]}");

        return userId;
    }

    private async Task<Guid> SeedOrderAsync()
    {
        var userId = await SeedUserAsync();
        var orderId = Guid.NewGuid();

        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO orders (id, user_id, price, status, kind, created_at_utc, updated_at_utc, is_deleted)
            VALUES ({0}, {1}, 900, 'PendingPayment', 'Purchase', now(), now(), false)
            """,
            orderId, userId);

        return orderId;
    }

    /// <summary>
    /// Money in the account. <c>forCustomer: false</c> is the unallocated case — declared, real, and
    /// named to nobody.
    /// </summary>
    private async Task<Guid> SeedPaymentAsync(
        decimal amount,
        Guid? orderId = null,
        bool forCustomer = true,
        PaymentKind kind = PaymentKind.Capture,
        PaymentStatus status = PaymentStatus.Succeeded,
        Guid? parentPaymentId = null)
    {
        await using var context = CreateContext();

        var payment = new Payment
        {
            Id = Guid.CreateVersion7(),
            Kind = kind,
            Provider = PaymentProviders.Declared,
            UserId = forCustomer ? await SeedUserAsync() : null,
            OrderId = orderId,
            ParentPaymentId = parentPaymentId,
            AmountUah = amount,
            AmountKopecks = (long)Math.Round(amount * 100),
            Currency = "UAH",
            Status = status,
            PaidAtUtc = status == PaymentStatus.Succeeded ? TestClock.UtcNow : null,
            CreatedByUserName = "finance",
            IsAutomatic = true,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        };

        context.Payments.Add(payment);
        await context.SaveChangesAsync();
        return payment.Id;
    }
}

using FluentAssertions;
using FuelFlow.Features.Feedback;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Settlement;
using FuelFlow.Features.Settlement.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Security;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Real-Postgres coverage for the settlement handler: the thing that turns a Telegram message into
/// money.
/// <para>
/// Three properties matter more than the rest, and each has tests of its own. The sender allow list
/// is the boundary — the webhook secret proves Telegram sent the delivery and says nothing about who
/// sent it. The (chat_id, message_id) unique index is what makes a Telegram redelivery harmless,
/// which for this table is the difference between a duplicate message and money twice. And nothing
/// infers: an unmatched code, an ambiguous order set, or a disagreeing amount all end in the same
/// place — a real payment with no order, waiting for a person.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public class SettlementUpdateHandlerTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public SettlementUpdateHandlerTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private const long AllowedChat = -1001234567890;
    private const long AllowedSender = 555000111;
    private const long StrangerSender = 999000222;

    private ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    private SettlementUpdateHandler CreateHandler(TelegramSettlementOptions options, ApplicationDbContext context) =>
        new(context, Options.Create(options), NullLogger<SettlementUpdateHandler>.Instance);

    /// <summary>
    /// A working configuration. <see cref="AllowedSender"/> is always on the list; extra senders are
    /// for the tests that need a second name. It is deliberately NOT an empty default: an allow list
    /// with nobody on it is a valid configuration that accepts nothing, and it would make every test
    /// here pass for the wrong reason.
    /// </summary>
    private static TelegramSettlementOptions Configured(long chat = AllowedChat, params long[] extraSenders) => new()
    {
        Enabled = true,
        BotToken = "test-token",
        WebhookSecret = "secret",
        AllowedChatId = chat,
        AllowedUserIds = [AllowedSender, .. extraSenders],
    };

    private static string NewPhone() => "+380" + Random.Shared.Next(100000000, 999999999).ToString();

    private async Task<(Guid UserId, string Code)> SeedCustomerAsync(ApplicationDbContext context)
    {
        var roleId = await context.Roles.Where(r => r.Name == SeedRoles.UserName).Select(r => r.Id).FirstAsync();
        var now = TestClock.UtcNow;
        var code = NewCode();

        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = NewPhone(),
            RoleId = roleId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            IsActive = true,
            // Fixed so the test asserts against a known value rather than reading back whatever the
            // database issued. The uniqueness of generated codes is proven by SettlementCodeTests.
            SettlementCode = code,
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();
        return (user.Id, code);
    }

    private async Task<Guid> SeedOrderAsync(ApplicationDbContext context, Guid userId, decimal price)
    {
        var order = new Order
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Price = price,
            Status = OrderStatus.PendingPayment,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow,
        };

        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }

    private static TelegramUpdate Message(long messageId, string text, long chat = AllowedChat, long sender = AllowedSender) => new()
    {
        UpdateId = messageId * 10,
        Message = new TelegramMessage
        {
            MessageId = messageId,
            Text = text,
            Date = TestClock.Now.ToUnixTimeSeconds(),
            From = new TelegramUser { Id = sender, FirstName = "Finance", LastName = "Ops" },
            Chat = new TelegramChat { Id = chat, Type = "supergroup" },
        },
    };

    /// <summary>
    /// A distinct code per test. The column is uniquely indexed across every row, and every test in
    /// this class shares one database - so a fixed code would make the second test fail on the first
    /// one's row rather than on anything it is actually asserting.
    /// </summary>
    private static string NewCode()
    {
        const string alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
        return string.Create(8, alphabet, static (span, chars) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = chars[Random.Shared.Next(chars.Length)];
            }
        });
    }

    /// <summary>
    /// Reads back what this class's tests wrote, scoped to what each test owns.
    /// <para>
    /// The fixture gives every CLASS a private database, not every test, so these tables accumulate
    /// across the class. An unscoped <c>SingleAsync()</c> would therefore assert about whatever the
    /// earlier tests left behind, and would fail for a reason that has nothing to do with the test
    /// being run. Message ids and amounts are unique per test by construction, so scoping by them
    /// reads this test's row and nothing else.
    /// </para>
    /// </summary>
    private Task<SettlementMessage> TheMessage(ApplicationDbContext context, long messageId) =>
        context.SettlementMessages.AsNoTracking().SingleAsync(m => m.MessageId == messageId);

    private Task<Payment> ThePayment(ApplicationDbContext context, decimal amount) =>
        context.Payments.AsNoTracking().SingleAsync(p => p.AmountUah == amount);

    // --------------------------------------------------------------------------------------------
    // The boundary
    // --------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_unlisted_sender_creates_no_payment_and_the_attempt_is_recorded()
    {
        // The whole point of the allow list. The webhook secret proved Telegram delivered this; it did
        // not prove who pressed send, and anyone can message a bot.
        using var context = CreateContext();
        var (userId, code) = await SeedCustomerAsync(context);
        await SeedOrderAsync(context, userId, 1500m);

        await CreateHandler(Configured(), context).HandleAsync(
            Message(1001, $"1501 {code}", sender: StrangerSender), default);

        (await context.Payments.CountAsync(p => p.AmountUah == 1501m)).Should().Be(0,
            "a stranger's message must not move money, however well-formed it is");

        var row = await TheMessage(context, 1001);
        row.Outcome.Should().Be(SettlementOutcomes.RejectedSender);
        row.TelegramUserId.Should().Be(StrangerSender, "a refusal that cannot name the sender names nothing");
        row.PaymentId.Should().BeNull();
    }

    [Fact]
    public async Task An_empty_allow_list_accepts_nobody()
    {
        // Fails closed. An absent list read as "everybody" would make a missing setting the single
        // most dangerous state the configuration can be in - and the one most likely to be reached
        // while debugging.
        var options = Configured();
        options.AllowedUserIds = [];

        options.IsConfigured.Should().BeFalse();
        options.MayAccept(AllowedSender).Should().BeFalse();
        options.MayAccept(0).Should().BeFalse();
    }

    [Fact]
    public async Task A_message_from_another_chat_is_refused()
    {
        using var context = CreateContext();
        var (userId, code) = await SeedCustomerAsync(context);

        await CreateHandler(Configured(), context).HandleAsync(
            Message(1002, $"1502 {code}", chat: -1009999999999), default);

        (await context.Payments.CountAsync(p => p.AmountUah == 1502m)).Should().Be(0);
        (await TheMessage(context, 1002)).Outcome.Should().Be(SettlementOutcomes.RejectedChat);
    }

    [Fact]
    public async Task An_unconfigured_channel_writes_nothing_at_all()
    {
        // Not even a rejection row: a channel we are not serving should be silent, and filling the
        // table with its traffic would bury the refusals that matter.
        using var context = CreateContext();
        var (userId, code) = await SeedCustomerAsync(context);

        var options = Configured();
        options.Enabled = false;

        await CreateHandler(options, context).HandleAsync(Message(1003, $"1503 {code}"), default);

        (await context.SettlementMessages.CountAsync(m => m.MessageId == 1003)).Should().Be(0);
        (await context.Payments.CountAsync(p => p.AmountUah == 1503m)).Should().Be(0);
    }

    // --------------------------------------------------------------------------------------------
    // The happy path, and the one place attaching is not a guess
    // --------------------------------------------------------------------------------------------

    [Fact]
    public async Task One_open_order_for_exactly_this_amount_is_attached()
    {
        using var context = CreateContext();
        var (userId, code) = await SeedCustomerAsync(context);
        var orderId = await SeedOrderAsync(context, userId, 1504m);

        await CreateHandler(Configured(), context).HandleAsync(Message(1004, $"1504 {code}"), default);

        var row = await TheMessage(context, 1004);
        row.Outcome.Should().Be(SettlementOutcomes.Accepted);
        row.ResolvedUserId.Should().Be(userId);
        row.PaymentId.Should().NotBeNull();

        var payment = await ThePayment(context, 1504m);
        payment.OrderId.Should().Be(orderId);
        payment.UserId.Should().Be(userId);
        payment.Provider.Should().Be(PaymentProviders.Declared);
        payment.Status.Should().Be(PaymentStatus.Succeeded);
        payment.PaidAtUtc.Should().NotBeNull();
    }

    // --------------------------------------------------------------------------------------------
    // Everything that must NOT be guessed
    // --------------------------------------------------------------------------------------------

    [Fact]
    public async Task No_open_order_holds_the_money_for_the_customer()
    {
        // The advance. The payment exists, belongs to the customer, and has no order — which is what
        // keeps it out of revenue until the fuel is delivered.
        using var context = CreateContext();
        var (userId, code) = await SeedCustomerAsync(context);

        await CreateHandler(Configured(), context).HandleAsync(Message(1005, $"1505 {code}"), default);

        var row = await TheMessage(context, 1005);
        row.Outcome.Should().Be(SettlementOutcomes.Unallocated);
        row.RejectionReason.Should().Contain("no order waiting");

        var payment = await ThePayment(context, 1505m);
        payment.OrderId.Should().BeNull();
        payment.UserId.Should().Be(userId);
    }

    [Fact]
    public async Task An_amount_that_disagrees_with_the_open_order_is_not_attached()
    {
        using var context = CreateContext();
        var (userId, code) = await SeedCustomerAsync(context);
        var orderId = await SeedOrderAsync(context, userId, 1506m);

        await CreateHandler(Configured(), context).HandleAsync(Message(1006, $"2006 {code}"), default);

        (await TheMessage(context, 1006)).RejectionReason.Should().Contain("different amount");

        (await ThePayment(context, 2006m)).OrderId.Should().BeNull();
        orderId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Two_open_orders_are_never_guessed_between()
    {
        using var context = CreateContext();
        var (userId, code) = await SeedCustomerAsync(context);
        await SeedOrderAsync(context, userId, 1507m);
        await SeedOrderAsync(context, userId, 1507m);

        await CreateHandler(Configured(), context).HandleAsync(Message(1007, $"1507 {code}"), default);

        (await TheMessage(context, 1007)).RejectionReason.Should().Contain("2 orders");

        (await ThePayment(context, 1507m)).OrderId.Should().BeNull();
    }

    [Fact]
    public async Task An_unknown_code_records_the_money_rather_than_dropping_it()
    {
        // The money arrived. Throwing it away because we could not place it would be the worst
        // outcome available; an unallocated payment is evidence, and an operator can resolve it.
        using var context = CreateContext();

        await CreateHandler(Configured(), context).HandleAsync(Message(1008, "1508 ZZZZZZZZ"), default);

        var row = await TheMessage(context, 1008);
        row.Outcome.Should().Be(SettlementOutcomes.Unallocated);
        row.Code.Should().Be("ZZZZZZZZ");
        row.AmountUah.Should().Be(1508m);
        row.ResolvedUserId.Should().BeNull();

        var payment = await ThePayment(context, 1508m);
        payment.OrderId.Should().BeNull();
        payment.UserId.Should().BeNull();
    }

    [Fact]
    public async Task A_message_that_does_not_parse_is_recorded_without_a_payment()
    {
        using var context = CreateContext();

        await CreateHandler(Configured(), context).HandleAsync(Message(1009, "перевірка, дзвінок"), default);

        var row = await TheMessage(context, 1009);
        row.Outcome.Should().Be(SettlementOutcomes.Unparseable);
        row.RejectionReason.Should().NotBeNullOrWhiteSpace();
        row.AmountUah.Should().BeNull();

        // No amount was parsed, so there is nothing to scope a payment query by - and there is none,
        // because an unparseable message never reaches the point of writing one.
        (await context.Payments.CountAsync(p => p.Provider == PaymentProviders.Declared && p.CreatedByUserName == null))
            .Should().Be(0);
    }

    // --------------------------------------------------------------------------------------------
    // Idempotency
    // --------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_redelivery_of_the_same_message_creates_no_second_payment()
    {
        // Telegram redelivers an update on any non-2xx. Here that is not a duplicate message — it is
        // money twice, which is why the uniqueness is in the database and not in the handler.
        using var context = CreateContext();
        var (userId, code) = await SeedCustomerAsync(context);
        await SeedOrderAsync(context, userId, 1510m);

        var handler = CreateHandler(Configured(), context);
        var update = Message(1010, $"1510 {code}");

        await handler.HandleAsync(update, default);
        await handler.HandleAsync(update, default);
        await handler.HandleAsync(update, default);

        (await context.Payments.CountAsync(p => p.AmountUah == 1510m)).Should().Be(1);
        (await context.SettlementMessages.CountAsync(m => m.MessageId == 1010)).Should().Be(1);
    }
}
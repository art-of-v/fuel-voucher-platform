using System.Text.Json;
using FluentAssertions;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Payments.Admin;
using FuelFlow.Features.Payments.Admin.AttachPayment;
using FuelFlow.Features.Payments.Admin.DismissPayment;
using FuelFlow.Features.Payments.Admin.GetCustomerByCode;
using FuelFlow.Features.Payments.Admin.GetUnallocatedPayments;
using FuelFlow.Features.Payments.Admin.SearchCustomers;
using FuelFlow.Persistence;
using FuelFlow.Payments.SharedModels;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Security;
using FuelFlow.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The operator's side of held money: see it, and place it.
/// <para>
/// These tests are mostly about refusals. An unallocated payment is money that belongs to somebody,
/// and every way this endpoint can be wrong — wrong customer, wrong order, an order that is already
/// settled — is a way to move a real person's money to the wrong real person. The refusals are the
/// feature.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public class AdminPaymentsControllerTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public AdminPaymentsControllerTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
    {
        var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(_fixture.ConnectionString)
                .Options, TestQrCipher.Instance);

        // Placing the full amount on an order now settles it, and a settle that cannot map its event
        // is refused loudly - which is the refusal a test should hit if the wiring were missing.
        TestJournal.SeedChart(context);
        return context;
    }

    private AdminPaymentsController CreateController(ApplicationDbContext context)
    {
        // The controller talks to handlers, and the handlers talk to the database: the screen is
        // built the way the rest of the platform is, so the rules in it are the rules the suite
        // can reach.
        var controller = new AdminPaymentsController(
            new GetUnallocatedPaymentsQueryHandler(context),
            new SearchCustomersQueryHandler(context),
            new GetCustomerByCodeQueryHandler(context),
            new AttachPaymentCommandHandler(
                context,
                new AuditEventService(context),
                TestJournal.TrySettleFor(context),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<AttachPaymentCommandHandler>.Instance),
            new DismissPaymentCommandHandler(
                context,
                new AuditEventService(context),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<DismissPaymentCommandHandler>.Instance))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(
                    new System.Security.Claims.ClaimsIdentity(
                        [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                         new System.Security.Claims.Claim("first_name", "qa")], "TestAuth")) },
            },
        };

        return controller;
    }

    private static string NewPhone() => "+380" + Random.Shared.Next(100000000, 999999999).ToString();

    private async Task<Guid> SeedCustomerAsync(
        ApplicationDbContext context, string code, string? firstName = null, string? lastName = null)
    {
        var roleId = await context.Roles.Where(r => r.Name == SeedRoles.UserName).Select(r => r.Id).FirstAsync();
        var now = TestClock.UtcNow;

        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = NewPhone(),
            RoleId = roleId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            IsActive = true,
            FirstName = firstName,
            LastName = lastName,
            SettlementCode = code,
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    private async Task<IReadOnlyList<CustomerSearchHit>> SearchCustomersAsync(
        ApplicationDbContext context, string search, int limit = 20)
    {
        var result = await CreateController(context).Customers(search, limit, default);
        var payload = (result.Result as OkObjectResult)?.Value as List<CustomerSearchHit>;
        return payload ?? new List<CustomerSearchHit>();
    }

    private async Task<Guid> SeedOrderAsync(ApplicationDbContext context, Guid userId, decimal price, OrderStatus status = OrderStatus.PendingPayment)
    {
        var order = new Order
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Price = price,
            Status = status,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow,
        };

        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }

    private async Task<Guid> SeedPaymentAsync(
        ApplicationDbContext context, Guid? userId, decimal amount, Guid? orderId = null)
    {
        var now = TestClock.UtcNow;
        var payment = new Payment
        {
            Id = Guid.CreateVersion7(),
            Kind = PaymentKind.Capture,
            Provider = PaymentProviders.Declared,
            UserId = userId,
            OrderId = orderId,
            AmountUah = amount,
            AmountKopecks = (long)(amount * 100),
            Currency = "UAH",
            Status = PaymentStatus.Succeeded,
            PaidAtUtc = now,
            CreatedByUserName = "finance",
            IsAutomatic = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        context.Payments.Add(payment);
        await context.SaveChangesAsync();
        return payment.Id;
    }

    /// <summary>
    /// Part of a held transfer committed to an order — the draw the balance rule writes instead of
    /// attaching the money, and the reason the worklist's amount is no longer the whole story.
    /// </summary>
    private async Task SeedDrawAsync(ApplicationDbContext context, Guid paymentId, Guid orderId, decimal amount)
    {
        context.PaymentAllocations.Add(new PaymentAllocation
        {
            Id = Guid.CreateVersion7(),
            PaymentId = paymentId,
            OrderId = orderId,
            AmountUah = amount,
            CreatedAtUtc = TestClock.UtcNow,
        });

        await context.SaveChangesAsync();
    }

    private static string NewCode()
    {
        const string alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
        return string.Create(8, alphabet, static (span, chars) =>
        {
            for (var i = 0; i < span.Length; i++) span[i] = chars[Random.Shared.Next(chars.Length)];
        });
    }

    // --------------------------------------------------------------------------------------------
    // The worklist
    // --------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_worklist_lists_money_with_no_order()
    {
        using var context = CreateContext();
        var code = NewCode();
        var userId = await SeedCustomerAsync(context, code);
        var paymentId = await SeedPaymentAsync(context, userId, 2001m);

        var response = await UnallocatedAsync(context);
        response.Data.Should().Contain(d => d.Id == paymentId && d.AmountUah == 2001m);
        response.Total.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// The worklist response. Returned as <c>Ok(...)</c>, so with <c>ActionResult&lt;T&gt;</c> the
    /// payload lands on <c>Result</c> rather than <c>Value</c> — reaching for the wrong one gives a
    /// null that fails a hundred lines away from the cause.
    /// </summary>
    private async Task<UnallocatedPaymentsResponse> UnallocatedAsync(ApplicationDbContext context)
    {
        var action = await CreateController(context).Unallocated(default);
        return (action.Result as OkObjectResult)?.Value as UnallocatedPaymentsResponse
            ?? throw new InvalidOperationException("the worklist did not answer with 200 OK");
    }

    [Fact]
    public async Task The_worklist_leaves_out_money_that_is_already_placed()
    {
        // A payment on an order is settled business as far as this screen is concerned. Including it
        // would bury the two rows that need a person in a list of everything.
        using var context = CreateContext();
        var code = NewCode();
        var userId = await SeedCustomerAsync(context, code);
        var orderId = await SeedOrderAsync(context, userId, 2002m);

        var paymentId = await SeedPaymentAsync(context, userId, 2002m);
        var payment = await context.Payments.FirstAsync(p => p.Id == paymentId);
        payment.OrderId = orderId;
        await context.SaveChangesAsync();

        var response = await UnallocatedAsync(context);

        response.Data.Should().NotContain(d => d.Id == paymentId);
    }

    // --------------------------------------------------------------------------------------------
    // Placing, and every way it can be wrong
    // --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Naming_the_customer_places_an_anonymous_payment()
    {
        using var context = CreateContext();
        var code = NewCode();
        var userId = await SeedCustomerAsync(context, code);
        var paymentId = await SeedPaymentAsync(context, userId: null, 2003m);

        var result = await CreateController(context).Attach(
            paymentId, new AttachPaymentRequest { UserId = userId }, default);

        result.Should().BeOfType<NoContentResult>();
        (await context.Payments.AsNoTracking().FirstAsync(p => p.Id == paymentId)).UserId.Should().Be(userId);
    }

    [Fact]
    public async Task Placing_the_full_amount_on_the_open_order_settles_it()
    {
        using var context = CreateContext();
        var code = NewCode();
        var userId = await SeedCustomerAsync(context, code);
        var orderId = await SeedOrderAsync(context, userId, 2004m);
        var paymentId = await SeedPaymentAsync(context, userId, 2004m);

        var result = await CreateController(context).Attach(
            paymentId, new AttachPaymentRequest { OrderId = orderId }, default);

        result.Should().BeOfType<NoContentResult>();
        (await context.Payments.AsNoTracking().FirstAsync(p => p.Id == paymentId)).OrderId.Should().Be(orderId);

        // The name of this test used to promise more than it checked: it read only the payment row, so
        // the screen could place money on an order and leave it unpaid, unfulfilled and unbooked and the
        // suite would call that settled. It is what stranded a real 862.00 ₴ on an order whose expired
        // invoice cancelled it (planning #867, production 2026-11-04).
        var settled = await context.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        settled.Status.Should().Be(OrderStatus.PendingFulfillment);

        var orderProbe = System.Text.Json.JsonSerializer.Serialize(new { orderId });
        (await context.OutboxEvents.AsNoTracking().CountAsync(
                e => e.EventType == OutboxEventType.OrderCreated
                     && EF.Functions.JsonContains(e.Payload, orderProbe)))
            .Should().Be(1, "placing the money is the act that hands the order to fulfilment");

        // The books say nothing. The money placed here is declared money - the settlement channel's -
        // and the promo ruling of 2026-11-07 says a sale it pays for books nothing. The order is
        // settled and handed to fulfilment; the books would otherwise show promo revenue.
        (await context.JournalEntries.AsNoTracking().CountAsync(j => j.SourceId == orderId))
            .Should().Be(0, "promo revenue is not revenue");

        // The acquirer never spoke about this money, so nothing may say that it did.
        settled.MonobankStatus.Should().BeNull();
        settled.LastWebhookProcessedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Placing_less_than_the_price_leaves_the_order_awaiting_the_rest()
    {
        // Half of a sale is real money that belongs on the order and is not a paid order. Settling on
        // this row would hand out fuel the customer has not paid for; refusing the row would lose the
        // money. So the money stays and the order waits.
        using var context = CreateContext();
        var code = NewCode();
        var userId = await SeedCustomerAsync(context, code);
        var orderId = await SeedOrderAsync(context, userId, 2005m);
        var paymentId = await SeedPaymentAsync(context, userId, 1005m);

        var result = await CreateController(context).Attach(
            paymentId, new AttachPaymentRequest { OrderId = orderId }, default);
        result.Should().BeOfType<NoContentResult>();

        (await context.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId))
            .Status.Should().Be(OrderStatus.PendingPayment);
        (await context.JournalEntries.AsNoTracking().CountAsync(j => j.SourceId == orderId))
            .Should().Be(0, "nothing was paid, so nothing may be in the books");
    }

    [Fact]
    public async Task An_order_belonging_to_someone_else_is_refused()
    {
        // The mistake this whole screen invites: attaching to the wrong customer because it was the
        // one that was open.
        using var context = CreateContext();
        var userA = await SeedCustomerAsync(context, NewCode());
        var userB = await SeedCustomerAsync(context, NewCode());
        var bOrderId = await SeedOrderAsync(context, userB, 2005m);
        var paymentId = await SeedPaymentAsync(context, userA, 2005m);

        var result = await CreateController(context).Attach(
            paymentId, new AttachPaymentRequest { OrderId = bOrderId }, default);

        result.Should().BeOfType<BadRequestObjectResult>();
        (await context.Payments.AsNoTracking().FirstAsync(p => p.Id == paymentId)).OrderId.Should().BeNull();
    }

    [Fact]
    public async Task Moving_a_payment_to_another_customer_is_refused()
    {
        using var context = CreateContext();
        var owner = await SeedCustomerAsync(context, NewCode());
        var other = await SeedCustomerAsync(context, NewCode());
        var paymentId = await SeedPaymentAsync(context, owner, 2006m);

        var result = await CreateController(context).Attach(
            paymentId, new AttachPaymentRequest { UserId = other }, default);

        result.Should().BeOfType<BadRequestObjectResult>("reassigning is a different act from placing an anonymous payment");
        (await context.Payments.AsNoTracking().FirstAsync(p => p.Id == paymentId)).UserId.Should().Be(owner);
    }

    [Fact]
    public async Task An_order_that_is_not_waiting_for_payment_is_refused()
    {
        using var context = CreateContext();
        var userId = await SeedCustomerAsync(context, NewCode());
        var paidOrderId = await SeedOrderAsync(context, userId, 2007m, OrderStatus.Paid);
        var paymentId = await SeedPaymentAsync(context, userId, 2007m);

        var result = await CreateController(context).Attach(
            paymentId, new AttachPaymentRequest { OrderId = paidOrderId }, default);

        result.Should().BeOfType<BadRequestObjectResult>();
        (await context.Payments.AsNoTracking().FirstAsync(p => p.Id == paymentId)).OrderId.Should().BeNull();
    }

    /// <summary>
    /// The worklist is a list of what can still be placed, and a declared transfer whose balance has been
    /// spent across orders is not placeable in full. Both numbers are on the row: what arrived is the
    /// evidence, what is free is the decision.
    /// </summary>
    [Fact]
    public async Task The_worklist_says_what_is_left_of_a_transfer_that_has_already_paid()
    {
        using var context = CreateContext();
        var userId = await SeedCustomerAsync(context, NewCode());
        var paidFor = await SeedOrderAsync(context, userId, 1500m, OrderStatus.PendingFulfillment);
        var paymentId = await SeedPaymentAsync(context, userId, 2014m);
        await SeedDrawAsync(context, paymentId, paidFor, 1500m);

        var response = await UnallocatedAsync(context);
        var row = response.Data.Single(d => d.Id == paymentId);

        row.AmountUah.Should().Be(2014m, "that is what finance declared, and the row is the record of it");
        row.FreeUah.Should().Be(514m,
            "1 500,00 ₴ of the transfer paid for an order already, and an operator who read only the "
            + "first number would place money that is committed");
    }

    /// <summary>
    /// The press that the balance rule makes impossible: naming a whole transfer to one order when part
    /// of it already answers for another.
    /// </summary>
    [Fact]
    public async Task A_transfer_that_has_paid_for_an_order_is_not_placed_on_a_second_one()
    {
        using var context = CreateContext();
        var userId = await SeedCustomerAsync(context, NewCode());
        var paidFor = await SeedOrderAsync(context, userId, 1200m, OrderStatus.PendingFulfillment);
        var waiting = await SeedOrderAsync(context, userId, 800m);
        var paymentId = await SeedPaymentAsync(context, userId, 2015m);
        await SeedDrawAsync(context, paymentId, paidFor, 1200m);

        var result = await CreateController(context).Attach(
            paymentId, new AttachPaymentRequest { OrderId = waiting }, default);

        // Without this refusal the answer is the database's trigger and the screen shows a 500 about a
        // rule the operator was never told.
        result.Should().BeOfType<BadRequestObjectResult>();

        (await context.Payments.AsNoTracking().FirstAsync(p => p.Id == paymentId)).OrderId.Should().BeNull();
        (await context.Orders.AsNoTracking().FirstAsync(o => o.Id == waiting))
            .Status.Should().Be(OrderStatus.PendingPayment, "a refusal leaves the order exactly as it was");
    }

    [Fact]
    public async Task A_request_naming_nothing_is_refused()
    {
        using var context = CreateContext();
        var userId = await SeedCustomerAsync(context, NewCode());
        var paymentId = await SeedPaymentAsync(context, userId, 2008m);

        var result = await CreateController(context).Attach(paymentId, new AttachPaymentRequest(), default);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Placing_the_same_payment_twice_is_a_no_op_rather_than_a_failure()
    {
        // A double-click should not look like a failure, and must not move the money twice. The second
        // press also cannot be refused for the order's own state, because the state it cites — the one
        // the first press moved — is the reason the press is a no-op.
        using var context = CreateContext();
        var userId = await SeedCustomerAsync(context, NewCode());
        var orderId = await SeedOrderAsync(context, userId, 2009m);
        var paymentId = await SeedPaymentAsync(context, userId, 2009m);

        var controller = CreateController(context);
        await controller.Attach(paymentId, new AttachPaymentRequest { OrderId = orderId }, default);
        var second = await controller.Attach(paymentId, new AttachPaymentRequest { OrderId = orderId }, default);

        second.Should().BeOfType<NoContentResult>();

        // The books say nothing, and that is the promo ruling of 2026-11-07: the money placed here is
        // declared money, and a sale it pays for books nothing. The idempotency this test is about is
        // therefore proved on the order and the hand-off to fulfilment, not on the receipt - there is
        // no receipt to post twice.
        (await context.JournalEntries.AsNoTracking().CountAsync(j => j.SourceId == orderId))
            .Should().Be(0, "promo revenue is not revenue");

        (await context.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId))
            .Status.Should().Be(OrderStatus.PendingFulfillment);
        (await context.OutboxEvents.AsNoTracking().CountAsync(e => e.EventType == OutboxEventType.OrderCreated))
            .Should().Be(1, "the hand-off to fulfilment happens once, however many presses name the order");
    }

    [Fact]
    public async Task Money_already_on_a_cancelled_order_is_reported_rather_than_marked_done()
    {
        // The shape production woke up with on 2026-11-04: the invoice's expiry cancelled the order
        // while the declared money sat on it (planning #867). Pressing place again cannot settle that,
        // and answering 204 would tell the operator the sale was completed. The refusal is the feature;
        // what to do with the money is a decision this screen is not allowed to make on its own.
        using var context = CreateContext();
        var userId = await SeedCustomerAsync(context, NewCode());
        var orderId = await SeedOrderAsync(context, userId, 2013m, OrderStatus.Cancelled);
        var paymentId = await SeedPaymentAsync(context, userId, 2013m, orderId: orderId);

        var result = await CreateController(context)
            .Attach(paymentId, new AttachPaymentRequest { OrderId = orderId }, default);

        var refusal = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        ((ProblemDetails)refusal.Value!).Detail.Should().Contain("Cancelled");

        (await context.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId))
            .Status.Should().Be(OrderStatus.Cancelled, "this screen does not revive an order by pressing place");
        (await context.JournalEntries.AsNoTracking().CountAsync(j => j.SourceId == orderId))
            .Should().Be(0, "no revenue is booked for fuel nobody will deliver");
    }

    [Fact]
    public async Task The_worklist_carries_the_customers_open_orders_to_pick_from()
    {
        // Without these the operator pastes an order UUID, which is not placing money — it is asking
        // them to trust an identifier. One query for the page, keyed by customer.
        using var context = CreateContext();
        var code = NewCode();
        var userId = await SeedCustomerAsync(context, code);
        var openId = await SeedOrderAsync(context, userId, 2011m, OrderStatus.PendingPayment);
        var paidId = await SeedOrderAsync(context, userId, 2012m, OrderStatus.Paid);
        await SeedPaymentAsync(context, userId, 2011m);

        var response = await UnallocatedAsync(context);

        response.OpenOrders.Should().Contain(o => o.Id == openId && o.Price == 2011m);
        response.OpenOrders.Should().NotContain(o => o.Id == paidId,
            "an order that is not waiting for payment is not a choice the backend will accept");
    }

    [Fact]
    public async Task A_settlement_code_resolves_to_its_customer()
    {
        using var context = CreateContext();
        var code = NewCode();
        var userId = await SeedCustomerAsync(context, code);

        // ActionResult (non-generic) returns the IActionResult directly - there is no .Result to unwrap.
        var ok = (await CreateController(context).CustomerByCode(code.ToLowerInvariant(), default))
            .Should().BeOfType<OkObjectResult>().Subject;

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        doc.RootElement.GetProperty("Id").GetGuid().Should().Be(userId);
    }

    [Fact]
    public async Task An_unmatched_code_explains_the_two_reasons_it_matched_nobody()
    {
        // "Not found" alone does not help: a mistyped code and a customer who has not registered are
        // different problems with different fixes.
        using var context = CreateContext();

        var result = (await CreateController(context).CustomerByCode("ZZZZZZZZ", default))
            .Should().BeOfType<NotFoundObjectResult>().Subject;

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result.Value));
        var error = doc.RootElement.GetProperty("detail").GetString();
        error.Should().Contain("mistyped").And.Contain("not registered");
    }

    [Fact]
    public async Task A_code_of_the_wrong_length_is_refused_before_it_is_looked_up()
    {
        using var context = CreateContext();

        (await CreateController(context).CustomerByCode("SHORT", default))
            .Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task The_customer_picker_finds_by_name_phone_or_code()
    {
        using var context = CreateContext();
        var code = NewCode();
        var userId = await SeedCustomerAsync(context, code, firstName: "Олена", lastName: "Ковальчук");

        (await SearchCustomersAsync(context, "Ковальчук")).Should().ContainSingle().Which.Id.Should().Be(userId);
        (await SearchCustomersAsync(context, "Олена")).Should().ContainSingle();
        (await SearchCustomersAsync(context, code)).Should().ContainSingle().Which.Id.Should().Be(userId);
    }

    [Fact]
    public async Task The_customer_picker_needs_two_characters()
    {
        // A one-character search matches most of the table, and the point of a bounded query is
        // defeated the moment the bound is the result set.
        using var context = CreateContext();
        await SeedCustomerAsync(context, NewCode(), firstName: "Олена");

        (await SearchCustomersAsync(context, "О")).Should().BeEmpty();
    }

    [Fact]
    public async Task The_customer_picker_is_bounded()
    {
        using var context = CreateContext();
        for (var i = 0; i < 5; i++)
        {
            await SeedCustomerAsync(context, NewCode(), firstName: "Тест", lastName: $"Клієнт{i}");
        }

        var hits = (await SearchCustomersAsync(context, "Клієнт", limit: 2)).ToList();

        hits.Should().HaveCount(2);
    }

    [Fact]
    public async Task An_exact_code_outranks_a_lucky_substring()
    {
        // Two customers can share a name fragment, and a third can have this text inside their code.
        // The certain match should not sit below the coincidence.
        using var context = CreateContext();
        var roleId = await context.Roles.Where(r => r.Name == SeedRoles.UserName).Select(r => r.Id).FirstAsync();
        var exactCode = NewCode();
        var substringHolder = "ZZ" + exactCode.Substring(2, 6);
        var now = TestClock.UtcNow;

        context.Users.Add(new User
        {
            Id = Guid.NewGuid(), PhoneNumber = NewPhone(), RoleId = roleId,
            CreatedAtUtc = now, UpdatedAtUtc = now, IsActive = true,
            FirstName = "Точний", LastName = "Код", SettlementCode = exactCode,
        });
        context.Users.Add(new User
        {
            Id = Guid.NewGuid(), PhoneNumber = NewPhone(), RoleId = roleId,
            CreatedAtUtc = now, UpdatedAtUtc = now, IsActive = true,
            FirstName = "Підрядок", LastName = "Код", SettlementCode = substringHolder,
        });
        await context.SaveChangesAsync();

        var hits = await SearchCustomersAsync(context, exactCode);

        hits.First().SettlementCode.Should().Be(exactCode);
    }

    [Fact]
    public async Task Placing_money_is_written_to_the_audit_log()
    {
        // Hand-placing money is exactly the action an auditor asks about: "who decided this was that
        // customer's?". The answer has to be a record, not a recollection.
        using var context = CreateContext();
        var userId = await SeedCustomerAsync(context, NewCode());
        var paymentId = await SeedPaymentAsync(context, userId: null, 2010m);

        await CreateController(context).Attach(paymentId, new AttachPaymentRequest { UserId = userId }, default);

        (await context.Set<AuditEventOutbox>()
                .AnyAsync(a => a.AggregateType == "Payment"
                            && a.AggregateId == paymentId.ToString()
                            && a.EventType == "PaymentPlaced"))
            .Should().BeTrue();
    }
}
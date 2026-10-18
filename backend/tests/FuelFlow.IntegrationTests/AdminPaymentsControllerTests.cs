using FluentAssertions;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Payments.Admin;
using FuelFlow.Persistence;
using FuelFlow.Payments.SharedModels;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Security;
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

    private ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    private AdminPaymentsController CreateController(ApplicationDbContext context)
    {
        var controller = new AdminPaymentsController(context, new AuditEventService(context))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new System.Security.Claims.ClaimsPrincipal(
                    new System.Security.Claims.ClaimsIdentity(
                        [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                         new System.Security.Claims.Claim("first_name", "QA")], "TestAuth")) },
            },
        };

        return controller;
    }

    private static string NewPhone() => "+380" + Random.Shared.Next(100000000, 999999999).ToString();

    private async Task<Guid> SeedCustomerAsync(ApplicationDbContext context, string code)
    {
        var roleId = await context.Roles.Where(r => r.Name == SeedRoles.UserName).Select(r => r.Id).FirstAsync();
        var now = DateTime.UtcNow;

        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = NewPhone(),
            RoleId = roleId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            IsActive = true,
            SettlementCode = code,
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    private async Task<Guid> SeedOrderAsync(ApplicationDbContext context, Guid userId, decimal price, OrderStatus status = OrderStatus.PendingPayment)
    {
        var order = new Order
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Price = price,
            Status = status,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }

    private async Task<Guid> SeedHeldPaymentAsync(ApplicationDbContext context, Guid? userId, decimal amount)
    {
        var now = DateTime.UtcNow;
        var payment = new Payment
        {
            Id = Guid.CreateVersion7(),
            Kind = PaymentKind.Capture,
            Provider = PaymentProviders.Declared,
            UserId = userId,
            OrderId = null,
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
        var paymentId = await SeedHeldPaymentAsync(context, userId, 2001m);

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

        var paymentId = await SeedHeldPaymentAsync(context, userId, 2002m);
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
        var paymentId = await SeedHeldPaymentAsync(context, userId: null, 2003m);

        var result = await CreateController(context).Attach(
            paymentId, new AttachPaymentRequest { UserId = userId }, default);

        result.Should().BeOfType<NoContentResult>();
        (await context.Payments.AsNoTracking().FirstAsync(p => p.Id == paymentId)).UserId.Should().Be(userId);
    }

    [Fact]
    public async Task Attaching_to_the_customers_open_order_settles_the_prepayment()
    {
        using var context = CreateContext();
        var code = NewCode();
        var userId = await SeedCustomerAsync(context, code);
        var orderId = await SeedOrderAsync(context, userId, 2004m);
        var paymentId = await SeedHeldPaymentAsync(context, userId, 2004m);

        var result = await CreateController(context).Attach(
            paymentId, new AttachPaymentRequest { OrderId = orderId }, default);

        result.Should().BeOfType<NoContentResult>();
        (await context.Payments.AsNoTracking().FirstAsync(p => p.Id == paymentId)).OrderId.Should().Be(orderId);
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
        var paymentId = await SeedHeldPaymentAsync(context, userA, 2005m);

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
        var paymentId = await SeedHeldPaymentAsync(context, owner, 2006m);

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
        var paymentId = await SeedHeldPaymentAsync(context, userId, 2007m);

        var result = await CreateController(context).Attach(
            paymentId, new AttachPaymentRequest { OrderId = paidOrderId }, default);

        result.Should().BeOfType<BadRequestObjectResult>();
        (await context.Payments.AsNoTracking().FirstAsync(p => p.Id == paymentId)).OrderId.Should().BeNull();
    }

    [Fact]
    public async Task A_request_naming_nothing_is_refused()
    {
        using var context = CreateContext();
        var userId = await SeedCustomerAsync(context, NewCode());
        var paymentId = await SeedHeldPaymentAsync(context, userId, 2008m);

        var result = await CreateController(context).Attach(paymentId, new AttachPaymentRequest(), default);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Placing_the_same_payment_twice_is_a_no_op_rather_than_a_failure()
    {
        // A double-click should not look like a failure, and must not move the money twice.
        using var context = CreateContext();
        var userId = await SeedCustomerAsync(context, NewCode());
        var orderId = await SeedOrderAsync(context, userId, 2009m);
        var paymentId = await SeedHeldPaymentAsync(context, userId, 2009m);

        var controller = CreateController(context);
        await controller.Attach(paymentId, new AttachPaymentRequest { OrderId = orderId }, default);
        var second = await controller.Attach(paymentId, new AttachPaymentRequest { OrderId = orderId }, default);

        second.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task Placing_money_is_written_to_the_audit_log()
    {
        // Hand-placing money is exactly the action an auditor asks about: "who decided this was that
        // customer's?". The answer has to be a record, not a recollection.
        using var context = CreateContext();
        var userId = await SeedCustomerAsync(context, NewCode());
        var paymentId = await SeedHeldPaymentAsync(context, userId: null, 2010m);

        await CreateController(context).Attach(paymentId, new AttachPaymentRequest { UserId = userId }, default);

        (await context.Set<AuditEventOutbox>()
                .AnyAsync(a => a.AggregateType == "Payment"
                            && a.AggregateId == paymentId.ToString()
                            && a.EventType == "PaymentPlaced"))
            .Should().BeTrue();
    }
}
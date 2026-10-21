using FluentAssertions;
using FuelFlow.Features.Acquiring;
using FuelFlow.Features.Acquiring.Reconciliation;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The half of the reconciliation act that compares us to the acquirer in the *other* direction:
/// orders we believe took money in the period whose invoice the acquirer never mentioned.
/// </summary>
/// <remarks>
/// <para>
/// The first implementation subtracted the statement's own invoice ids from the order list the
/// matcher had already been narrowed to <em>by those same ids</em>. That set difference is
/// empty by construction, so the table could never contain a row - the screen shipped, the
/// feature behind it could not fire, and nothing in the build noticed because an empty list is a
/// perfectly valid result.
/// </para>
/// <para>
/// Real PostgreSQL because the candidate set is a query with an <c>IgnoreQueryFilters</c> and a
/// period bound; an in-memory brand would answer a different question.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class StatementMissingOrdersIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public StatementMissingOrdersIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private static readonly DateTime PeriodStart = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodEnd = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    private ApplicationDbContext Context()
    {
        var scope = _fixture.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    }

    /// <summary>
    /// The fixture is shared across the assembly, so without this the invoices one test seeds are
    /// visible to the next - and a test asserting "nothing is missing" fails on someone else's row.
    /// </summary>
    private async Task ResetAsync()
    {
        await using var context = Context();
        await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "monobank_statement_entries" RESTART IDENTITY CASCADE""");
        await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "orders" RESTART IDENTITY CASCADE""");
    }

    private async Task<Guid> EnsureUserAsync(ApplicationDbContext context)
    {
        var id = Guid.NewGuid();
        context.Users.Add(new User
        {
            Id = id,
            PhoneNumber = $"+38{id:N}"[..20],
            IsActive = true,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow,
        });
        await context.SaveChangesAsync();
        return id;
    }

    private static Order Order(Guid userId, string? invoiceId, DateTime created, decimal price, MonobankMerchant? merchant)
    {
        return new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Price = price,
            Status = OrderStatus.Paid,
            MonobankInvoiceId = invoiceId,
            MonobankMerchant = merchant,
            MonobankStatus = MonobankStatus.Success,
            CreatedAtUtc = created,
            UpdatedAtUtc = created,
        };
    }

    private static MonobankStatementEntry Entry(string statementId, string? invoiceId, long kopecks)
    {
        return new MonobankStatementEntry
        {
            Id = Guid.NewGuid(),
            Merchant = MonobankMerchant.Live,
            StatementId = statementId,
            InvoiceId = invoiceId,
            Reference = "FuelFlow Order demo",
            AmountKopecks = kopecks,
            Ccy = 980,
            Status = "settled",
            AcquirerCreatedAtUtc = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc),
            FirstSeenAtUtc = TestClock.UtcNow,
            LastSeenAtUtc = TestClock.UtcNow,
        };
    }

    private async Task<StatementReconciliationResponse> ActAsync(ApplicationDbContext context)
    {
        var handler = new GetStatementReconciliationQueryHandler(context);
        return await handler.HandleAsync(new StatementReconciliationQuery
        {
            Merchant = MonobankMerchant.Live,
            FromUtc = PeriodStart,
            ToUtc = PeriodEnd,
        });
    }

    [Fact]
    public async Task Reports_an_order_the_acquirer_never_mentioned()
    {
        await ResetAsync();
        await using var setup = Context();
        var userId = await EnsureUserAsync(setup);
        var silent = Order(userId, "INV-SILENT", new DateTime(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc), 1200m, MonobankMerchant.Live);
        setup.Orders.AddRange(
            silent,
            Order(userId, "INV-REPORTED", new DateTime(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc), 900m, MonobankMerchant.Live));
        setup.MonobankStatementEntries.Add(Entry("ST-1", "INV-REPORTED", 90000));
        await setup.SaveChangesAsync();

        var act = await ActAsync(Context());

        // The whole point: we took the money, the acquirer says nothing about it.
        act.MissingFromStatement.Should().ContainSingle();
        act.MissingFromStatement[0].InvoiceId.Should().Be("INV-SILENT");
        act.MissingFromStatement[0].Amount.Should().Be(1200m);
    }

    [Fact]
    public async Task An_order_the_acquirer_did_report_is_not_reported_as_missing()
    {
        await ResetAsync();
        await using var setup = Context();
        var userId = await EnsureUserAsync(setup);
        setup.MonobankStatementEntries.Add(Entry("ST-2", "INV-KNOWN", 45000));
        setup.Orders.Add(Order(userId, "INV-KNOWN", new DateTime(2026, 10, 9, 9, 0, 0, DateTimeKind.Utc), 450m, MonobankMerchant.Live));
        await setup.SaveChangesAsync();

        var act = await ActAsync(Context());

        act.MissingFromStatement.Should().BeEmpty();
        act.Rows.Should().ContainSingle(r => r.Status == StatementMatchStatus.Matched);
    }

    [Fact]
    public async Task An_order_outside_the_period_is_not_reported_as_missing()
    {
        // The period is what makes this an act for October rather than a list of all our orders.
        await ResetAsync();
        await using var setup = Context();
        var userId = await EnsureUserAsync(setup);
        setup.Orders.Add(Order(userId, "INV-OLD", new DateTime(2026, 9, 28, 9, 0, 0, DateTimeKind.Utc), 700m, MonobankMerchant.Live));
        await setup.SaveChangesAsync();

        var act = await ActAsync(Context());

        act.MissingFromStatement.Should().BeEmpty();
    }
}
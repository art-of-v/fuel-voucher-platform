using FuelFlow.Features.Acquiring.Reconciliation;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Options;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Acquiring.Reconciliation;

/// <summary>
/// Builds the reconciliation act for one merchant and one period: the acquirer's statement on one
/// side, our orders and refunds on the other, and a verdict on every row.
/// </summary>
/// <remarks>
/// The period is a calendar month by default because that is what "October's act" has to mean to
/// whoever signs it off. It is deliberately independent of the import job's rolling window: the
/// window exists to catch late movement, the calendar month exists to be legible.
/// </remarks>
public sealed class GetStatementReconciliationQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetStatementReconciliationQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<StatementReconciliationResponse> HandleAsync(
        StatementReconciliationQuery query,
        CancellationToken cancellationToken = default)
    {
        var merchant = query.Merchant ?? MonobankMerchant.Live;

        var (fromUtc, toUtc) = ResolvePeriod(query);

        var entries = await _context.MonobankStatementEntries
            .AsNoTracking()
            .Where(e => e.Merchant == merchant
                     && e.AcquirerCreatedAtUtc != null
                     && e.AcquirerCreatedAtUtc >= fromUtc
                     && e.AcquirerCreatedAtUtc < toUtc)
            .OrderBy(e => e.AcquirerCreatedAtUtc)
            .ToListAsync(cancellationToken);

        var invoiceIds = entries
            .Select(e => e.InvoiceId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .ToList();

        // Both sides are narrowed to the invoices the statement actually mentions. Loading every
        // order for the period and joining in memory would put the whole order table behind a
        // reconciliation endpoint that is only ever read by an operator, once a month.
        var orders = invoiceIds.Count == 0
            ? new List<Order>()
            : await _context.Orders
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(o => o.MonobankInvoiceId != null && invoiceIds.Contains(o.MonobankInvoiceId))
                .ToListAsync(cancellationToken);

        var refunds = invoiceIds.Count == 0
            ? new List<Refund>()
            : await _context.Refunds
                .AsNoTracking()
                .Where(r => invoiceIds.Contains(r.InvoiceId))
                .ToListAsync(cancellationToken);

        var rows = StatementMatcher.Match(entries, orders, refunds);
        var summary = StatementMatcher.Summarise(rows);

        // Orders we believe took money in this period whose invoice the acquirer never mentioned.
        // The other direction of the disagreement: we believe we were paid, they say nothing.
        //
        // Deliberately NOT the `orders` list above. That list is narrowed to the invoices the
        // statement does mention - so the matcher never walks the whole order table - which means
        // subtracting the reported ids from it can only ever produce an empty set. The candidate
        // set has to be our own orders *for the period*, and that is what gives the table any
        // chance of showing a row.
        // A List, not a HashSet: EF Core expands a captured collection into a parameterised IN,
        // and it does that for a List. A HashSet is not a translatable shape, and the failure is
        // an exception at query time rather than a wrong answer.
        var reportedInvoiceIds = entries
            .Select(e => e.InvoiceId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .ToList();

        var missing = (await _context.Orders
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(o => o.MonobankInvoiceId != null
                        && o.CreatedAtUtc >= fromUtc
                        && o.CreatedAtUtc < toUtc
                        && (o.MonobankMerchant == null || o.MonobankMerchant == merchant))
            .Where(o => !reportedInvoiceIds.Contains(o.MonobankInvoiceId))
            .Select(o => new MissingFromStatementRow(
                o.Id,
                o.MonobankInvoiceId!,
                o.Price,
                o.Status,
                o.CreatedAtUtc))
            .ToListAsync(cancellationToken))
            // Sorted after materialising: an OrderBy on the projected record is not a shape EF
            // can translate, and the set is a single period's worth of orders.
            .OrderBy(r => r.OrderId)
            .ToList();

        return new StatementReconciliationResponse(
            merchant,
            fromUtc,
            toUtc,
            entries.Count,
            summary,
            rows,
            missing);
    }

    /// <summary>
    /// Explicit bounds win; otherwise the calendar month containing <paramref name="now"/>. Both
    /// bounds are made UTC and the end is pushed to the start of the next month, so a client asking
    /// for "October" gets a range that tiles rather than one that overlaps its neighbour.
    /// </summary>
    internal static (DateTime FromUtc, DateTime ToUtc) ResolvePeriod(
        StatementReconciliationQuery query,
        DateTime? now = null)
    {
        if (query.FromUtc is { } from && query.ToUtc is { } to)
        {
            return (DateTime.SpecifyKind(from, DateTimeKind.Utc), DateTime.SpecifyKind(to, DateTimeKind.Utc));
        }

        var reference = (query.Year, query.Month) is var (year, month) && year is not null && month is not null
            ? new DateTime(year.Value, month.Value, 1, 0, 0, 0, DateTimeKind.Utc)
            : new DateTime((now ?? DateTime.UtcNow).Year, (now ?? DateTime.UtcNow).Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var nextMonth = reference.AddMonths(1);

        return (reference, nextMonth);
    }
}

public sealed record StatementReconciliationQuery
{
    public MonobankMerchant? Merchant { get; init; }
    public int? Year { get; init; }
    public int? Month { get; init; }
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
}

/// <param name="MissingFromStatement">Orders in the period whose invoice the acquirer never
/// mentioned - the opposite direction of the disagreement to a mismatch.</param>
public sealed record StatementReconciliationResponse(
    MonobankMerchant Merchant,
    DateTime FromUtc,
    DateTime ToUtc,
    int StatementRowCount,
    StatementMatchSummary Summary,
    IReadOnlyList<StatementMatchRow> Rows,
    IReadOnlyList<MissingFromStatementRow> MissingFromStatement);

public sealed record MissingFromStatementRow(
    Guid OrderId,
    string InvoiceId,
    decimal Amount,
    OrderStatus Status,
    DateTime CreatedAtUtc);
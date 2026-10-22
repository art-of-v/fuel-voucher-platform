using FuelFlow.Features.Acquiring.Reconciliation;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.Payments.SharedModels;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Time;
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
                .IncludingSoftDeleted()
                .Where(o => o.MonobankInvoiceId != null && invoiceIds.Contains(o.MonobankInvoiceId))
                .ToListAsync(cancellationToken);

        // The refunds the act needs, in one query, and they are two sets:
        //
        //   - the refunds whose invoice the statement mentions, so a row that no order of ours
        //     carries can still be reported against the refund that gave the invoice back;
        //   - the refunds we issued inside the period, which are the act's own refunds section.
        //
        // The first is narrowed by the statement's own ids and the second by the period, so the
        // union is still two small sets. The period filter here is deliberately a SUPERSET of
        // `StatementRefunds.IsIssuedInPeriod` - it does not look at the status - so that a change to
        // which statuses count as issued can never lose a row to a query narrower than the rule that
        // decides what the act reports.
        var refunds = await _context.Payments
            .AsNoTracking()
            .Where(p => p.Kind == PaymentKind.Refund
                     && (invoiceIds.Contains(p.ProviderInvoiceId)
                         || ((p.PaidAtUtc ?? p.CreatedAtUtc) >= fromUtc
                             && (p.PaidAtUtc ?? p.CreatedAtUtc) < toUtc)))
            .ToListAsync(cancellationToken);

        // The refunds of the period, and the two lookups the act needs to say which payment each of
        // them gives back against: the order it belongs to and the capture it reverses.
        var issued = refunds
            .Where(r => StatementRefunds.IsIssuedInPeriod(r, fromUtc, toUtc))
            .ToList();

        var issuedOrderIds = issued
            .Where(r => r.OrderId.HasValue)
            .Select(r => r.OrderId!.Value)
            .Distinct()
            .ToList();

        var issuedOrders = issuedOrderIds.Count == 0
            ? new List<Order>()
            : await _context.Orders
                .AsNoTracking()
                // A deleted order must not hide the refund that belongs to it: the money went back
                // either way, and the act is a record of money.
                .IncludingSoftDeleted()
                .Where(o => issuedOrderIds.Contains(o.Id))
                .ToListAsync(cancellationToken);

        var captureIds = issued
            .Where(r => r.ParentPaymentId.HasValue)
            .Select(r => r.ParentPaymentId!.Value)
            .Distinct()
            .ToList();

        var issuedCaptures = captureIds.Count == 0
            ? new List<Payment>()
            : await _context.Payments
                .AsNoTracking()
                .Where(p => captureIds.Contains(p.Id))
                .ToListAsync(cancellationToken);

        var refundsIssued = StatementRefunds.Compose(refunds, issuedOrders, issuedCaptures, fromUtc, toUtc);

        var rows = StatementMatcher.Match(entries, orders, refunds);
        var summary = StatementMatcher.Summarise(rows, refundsIssued);

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
            .IncludingSoftDeleted()
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
            missing,
            refundsIssued);
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
            : new DateTime((now ?? SystemClock.UtcNow).Year, (now ?? SystemClock.UtcNow).Month, 1, 0, 0, 0, DateTimeKind.Utc);

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
/// <param name="RefundsIssued">The refunds we issued in the period - the act's own side of the
/// reconciliation, beside the acquirer's statement. See <see cref="StatementRefunds"/>.</param>
public sealed record StatementReconciliationResponse(
    MonobankMerchant Merchant,
    DateTime FromUtc,
    DateTime ToUtc,
    int StatementRowCount,
    StatementMatchSummary Summary,
    IReadOnlyList<StatementMatchRow> Rows,
    IReadOnlyList<MissingFromStatementRow> MissingFromStatement,
    IReadOnlyList<IssuedRefundRow> RefundsIssued);

public sealed record MissingFromStatementRow(
    Guid OrderId,
    string InvoiceId,
    decimal Amount,
    OrderStatus Status,
    DateTime CreatedAtUtc);

/// <param name="RefundId">The refund payment's own id.</param>
/// <param name="OrderId">The order the refund belongs to.</param>
/// <param name="InvoiceId">The acquirer invoice of the payment the refund reverses - so the reader
/// can see which payment it belongs to, which is the detail the statement cannot give us: a
/// reversal arrives with no invoice id of its own.</param>
/// <param name="Amount">What went back, in UAH.</param>
/// <param name="IssuedAtUtc">When the money left - the acquirer's own date for the reversal.</param>
/// <param name="ReversedPaymentAtUtc">When we took the money it gives back against.</param>
/// <param name="ReversesEarlierPeriodPayment">True when that payment belongs to an earlier period
/// than the one this act covers - the case that makes a net figure meaningless, because the two
/// sides would not be talking about the same money.</param>
/// <param name="Note">A human-readable reason, in the act's language, for anything the reader would
/// otherwise have to work out from the dates.</param>
public sealed record IssuedRefundRow(
    Guid RefundId,
    Guid? OrderId,
    string? InvoiceId,
    decimal Amount,
    DateTime IssuedAtUtc,
    DateTime? ReversedPaymentAtUtc,
    bool ReversesEarlierPeriodPayment,
    string? Note);

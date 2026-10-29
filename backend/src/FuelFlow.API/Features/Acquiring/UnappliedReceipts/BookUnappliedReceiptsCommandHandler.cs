using FuelFlow.Features.Ledger;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FuelFlow.Features.Acquiring.UnappliedReceipts;

/// <summary>
/// Books acquirer money that no order of ours accounts for, as an unapplied receipt.
/// </summary>
/// <remarks>
/// <para>
/// The gap this closes is the one behind #780: a capture reaches the acquirer's statement, no
/// <c>payments</c> row and no order exist for its invoice, and the books therefore show less bank
/// than the statement does - 2.00 UAH in October 2026. Nothing in the system refused the money, and
/// nothing recorded it either.
/// </para>
/// <para>
/// The booking is deliberately a <b>liability</b>, not revenue: Dr 311 bank / Cr 681 advances
/// received. We hold money whose owner, purpose and fate are undecided - it may be a mistaken charge
/// that is refunded, or a prepayment for something that never became an order. Revenue would be a
/// claim the business has not earned, and a VAT split would assert a taxable supply that has not
/// happened, so neither is posted here. What happens next is an operator's decision, recorded in the
/// close document, and it is a reclassification out of 681 rather than a second receipt.
/// </para>
/// <para>
/// A person runs this, and the person is <c>PostedByUserId</c>. Posting money nobody ordered is an
/// accounting decision, and the journal's posted-by column is where that decision is answerable.
/// The payment row and the entry are written in one transaction: a payment with no entry would be
/// money recorded operationally and missing from the books, which is the state this exists to end.
/// </para>
/// <para>
/// <b>Only the Live merchant.</b> Sandbox payments move no money and must never reach the ledger.
/// </para>
/// </remarks>
public sealed class BookUnappliedReceiptsCommandHandler
{
    private const string SourceEvent = "payment.received.unapplied";

    private readonly ApplicationDbContext _context;
    private readonly JournalPostingService _journal;
    private readonly ILogger<BookUnappliedReceiptsCommandHandler> _logger;

    public BookUnappliedReceiptsCommandHandler(
        ApplicationDbContext context,
        JournalPostingService journal,
        ILogger<BookUnappliedReceiptsCommandHandler> logger)
    {
        _context = context;
        _journal = journal;
        _logger = logger;
    }

    public async Task<BookUnappliedReceiptsResponse> HandleAsync(
        BookUnappliedReceiptsCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.BookedByUserId == Guid.Empty)
            throw new LedgerPostingException("Booking an unapplied receipt must name the operator who booked it.");
        if (command.ToDate < command.FromDate)
            throw new LedgerPostingException(
                $"The period ends before it starts: {command.FromDate:yyyy-MM-dd} .. {command.ToDate:yyyy-MM-dd}.");

        // Whole days in UTC, matching how the statement's own operation time is stored and how the
        // reconciliation act resolves its period.
        var fromUtc = DateTime.SpecifyKind(command.FromDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(command.ToDate.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        var now = SystemClock.UtcNow;

        // What the last attempt set out to book, so verifySucceeded can ask the database whether that
        // attempt landed. Written inside the body because the candidates are re-read there.
        IReadOnlyList<UnbookedCapture> attempted = [];

        // The transaction runs through ReplaySafeTransaction, so the execution strategy may replay
        // the body; a transaction opened by hand throws at the first save inside it on this provider.
        var booked = await _context.ExecuteReplaySafeAsync<IReadOnlyList<BookedUnappliedReceipt>>(
            async ct =>
            {
                // Re-read inside the transaction rather than before it: the candidates are a claim
                // about the database ("no payment holds this invoice"), and a claim read outside the
                // transaction can be stale by the time the write happens - two operators booking the
                // same charge would then collide on the invoice's unique index instead of one of
                // them finding nothing to do.
                attempted = await UnappliedReceipts.FindAsync(
                    _context, MonobankMerchant.Live, fromUtc, toUtc, ct);

                var results = new List<BookedUnappliedReceipt>(attempted.Count);

                foreach (var candidate in attempted)
                {
                    var payment = new Payment
                    {
                        Id = Guid.CreateVersion7(),
                        // NULL on both: this is money we hold that is not yet against a sale (see Payment.OrderId).
                        // An operator allocates it or refunds it; nothing in this path invents an owner.
                        OrderId = null,
                        UserId = null,
                        Kind = PaymentKind.Capture,
                        Provider = WebhookEventIdentity.Monobank,
                        Merchant = MonobankMerchant.Live,
                        ProviderInvoiceId = candidate.InvoiceId,
                        ProviderPaymentId = candidate.Rrn,
                        AmountUah = candidate.AmountUah,
                        AmountKopecks = Money.ToKopecks(candidate.AmountUah),
                        ProviderAmountKopecks = Money.ToKopecks(candidate.AmountUah),
                        Currency = "UAH",
                        Status = PaymentStatus.Succeeded,
                        // The acquirer's financial operation time, never "now": for a VAT payer this is
                        // the reporting-period boundary, and the row exists to record when the money moved.
                        PaidAtUtc = candidate.PaidAtUtc,
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    };

                    _context.Payments.Add(payment);
                    await _context.SaveChangesAsync(ct);

                    var entry = await _journal.PostAsync(
                        new PostJournalEntryRequest
                        {
                            SourceEvent = SourceEvent,
                            SourceType = JournalSourceType.Payment,
                            // Keyed on the payment, so a second run of this command finds the entry
                            // already there instead of writing an identical second one.
                            SourceId = payment.Id,
                            EntryDate = DateOnly.FromDateTime(candidate.PaidAtUtc),
                            AmountUah = candidate.AmountUah,
                            Description = $"Надходження без замовлення: інвойс {candidate.InvoiceId}",
                            PostedByUserId = command.BookedByUserId,
                        },
                        ct);

                    results.Add(new BookedUnappliedReceipt(
                        candidate.InvoiceId,
                        candidate.AmountUah,
                        candidate.PaidAtUtc,
                        payment.Id,
                        entry.EntryNo,
                        candidate.StatementRows));
                }

                return results;
            },
            cancellationToken,
            ct => AllBookedAsync(attempted, ct));

        if (booked.Count == 0)
        {
            _logger.LogInformation(
                "No unbooked {Merchant} captures in [{From} - {To}); nothing to book",
                MonobankMerchant.Live, command.FromDate, command.ToDate);
            return new BookUnappliedReceiptsResponse(command.FromDate, command.ToDate, 0, []);
        }

        // After the commit, not inside the body: a replayed attempt must not repeat a log line that
        // reads like a second booking of the same money.
        foreach (var receipt in booked)
        {
            _logger.LogWarning(
                "Booked {Amount} UAH received on invoice {InvoiceId} ({PaidAt:u}) with no order as an "
                + "unapplied receipt (payment {PaymentId}, journal entry {EntryNo})",
                receipt.AmountUah, receipt.InvoiceId, receipt.PaidAtUtc, receipt.PaymentId, receipt.EntryNo);
        }

        return new BookUnappliedReceiptsResponse(
            command.FromDate, command.ToDate, booked.Count, booked);
    }

    /// <summary>
    /// Did the attempt whose commit failed land anyway?
    /// </summary>
    /// <remarks>
    /// A failed commit is ambiguous - the database may have taken it and lost the answer - and
    /// replaying it would book the same money twice. Both halves are asked for, a payment row and a
    /// posted entry, because the body writes them in one transaction: finding only one of them is
    /// not a success, it is a state that should not exist and must not be papered over.
    /// </remarks>
    private async Task<bool> AllBookedAsync(
        IReadOnlyList<UnbookedCapture> candidates,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            // Nothing was written, so an ambiguous commit has nothing to confirm or repeat.
            return true;
        }

        var invoiceIds = candidates.Select(c => c.InvoiceId).ToList();

        var paymentIds = await _context.Payments
            .AsNoTracking()
            .Where(p => p.Provider == WebhookEventIdentity.Monobank
                     && p.Kind == PaymentKind.Capture
                     && p.ProviderInvoiceId != null
                     && invoiceIds.Contains(p.ProviderInvoiceId))
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        if (paymentIds.Count < candidates.Count)
        {
            return false;
        }

        var posted = await _context.JournalEntries
            .AsNoTracking()
            .CountAsync(
                e => e.SourceEvent == SourceEvent
                     && e.SourceType == JournalSourceType.Payment
                     && e.SourceId != null
                     && paymentIds.Contains(e.SourceId.Value)
                     && e.Status == JournalEntryStatus.Posted,
                cancellationToken);

        return posted >= candidates.Count;
    }
}

/// <param name="BookedByUserId">The operator standing behind the posting; the journal records it.</param>
public sealed record BookUnappliedReceiptsCommand(DateOnly FromDate, DateOnly ToDate, Guid BookedByUserId)
{
    /// <summary>The calendar month containing <paramref name="today"/>, unless both ends are given.</summary>
    public static BookUnappliedReceiptsCommand ForMonth(DateOnly? from, DateOnly? to, Guid bookedBy, DateOnly today)
    {
        if (from is { } f && to is { } t)
        {
            return new BookUnappliedReceiptsCommand(f, t, bookedBy);
        }

        var start = new DateOnly(today.Year, today.Month, 1);
        return new BookUnappliedReceiptsCommand(start, start.AddMonths(1).AddDays(-1), bookedBy);
    }
}

/// <param name="EntryNo">The journal entry the money was posted under.</param>
/// <param name="StatementRows">See <see cref="UnbookedCapture.StatementRows"/>.</param>
public sealed record BookedUnappliedReceipt(
    string InvoiceId,
    decimal AmountUah,
    DateTime PaidAtUtc,
    Guid PaymentId,
    long EntryNo,
    int StatementRows);

public sealed record BookUnappliedReceiptsResponse(
    DateOnly FromDate,
    DateOnly ToDate,
    int CandidateCount,
    IReadOnlyList<BookedUnappliedReceipt> Booked);

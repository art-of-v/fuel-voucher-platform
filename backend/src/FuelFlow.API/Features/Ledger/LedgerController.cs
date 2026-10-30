using System.Security.Claims;
using FuelFlow.Features.Acquiring.UnappliedReceipts;
using FuelFlow.Features.Ledger.GetTrialBalance;
using FuelFlow.Features.Ledger.ManualEntries;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Errors;
using FuelFlow.SharedKernel.Time;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Ledger;

/// <summary>
/// The ledger's operator surface: the close check, and the corrections a close needs.
/// </summary>
/// <remarks>
/// Separate from <c>api/admin/reconciliation/statement</c> on purpose. That act compares the acquirer
/// against our orders, which is a reconciliation of business facts; this is the books themselves -
/// what is posted, what is still a draft, and whether the bank account agrees with the bank.
/// <para>
/// The capability is per action, not per controller: reading the trial balance is the read surface;
/// booking a receipt or posting a manual adjustment writes money into the books and carries the
/// money capability.
/// </para>
/// </remarks>
[ApiController]
[Route("api/admin/ledger")]
public sealed class LedgerController : ControllerBase
{
    private readonly GetTrialBalanceQueryHandler _trialBalance;
    private readonly BookUnappliedReceiptsCommandHandler _bookUnapplied;
    private readonly PostManualJournalEntryCommandHandler _manualEntries;

    public LedgerController(
        GetTrialBalanceQueryHandler trialBalance,
        BookUnappliedReceiptsCommandHandler bookUnapplied,
        PostManualJournalEntryCommandHandler manualEntries)
    {
        _trialBalance = trialBalance;
        _bookUnapplied = bookUnapplied;
        _manualEntries = manualEntries;
    }

    /// <summary>The close check as JSON. Defaults to the current calendar month.</summary>
    [HttpGet("trial-balance")]
    [Authorize(Policy = AuthPolicies.Staff)]
    [ProducesResponseType(typeof(TrialBalanceResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTrialBalance(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        var report = await _trialBalance.HandleAsync(
            new TrialBalanceQuery { FromDate = from, ToDate = to }, cancellationToken);

        return Ok(report);
    }

    /// <summary>The close check as CSV, for the accountant's spreadsheet.</summary>
    [HttpGet("trial-balance/csv")]
    [Authorize(Policy = AuthPolicies.Staff)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "text/csv")]
    public async Task<IActionResult> GetTrialBalanceCsv(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        var report = await _trialBalance.HandleAsync(
            new TrialBalanceQuery { FromDate = from, ToDate = to }, cancellationToken);

        var name = FormattableString.Invariant($"trial-balance-{report.FromDate:yyyy-MM}.csv");
        return File(TrialBalanceCsv.RenderBytes(report), "text/csv", name);
    }

    /// <summary>
    /// Books the Live merchant's received captures that have no payment row as unapplied receipts
    /// (Dr 311 / Cr 681), so the bank account matches the acquirer's statement (#780).
    /// </summary>
    /// <remarks>
    /// A POST because it writes money into the books, and the operator who calls it is recorded as
    /// the posting's author. Idempotent: an invoice already booked is not in the candidate set, and
    /// the entry is keyed on the payment either way.
    /// </remarks>
    [HttpPost("unapplied-receipts")]
    [Authorize(Policy = AuthPolicies.MoneyMovement)]
    [ProducesResponseType(typeof(BookUnappliedReceiptsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> BookUnappliedReceipts(
        [FromBody] BookUnappliedReceiptsRequest? request,
        CancellationToken cancellationToken)
    {
        if (CurrentUserId() is not { } userId)
        {
            // No body-less 401: the client reads one problem document, and an empty one leaves it
            // guessing (planning finding C-6).
            return ApiError.Unauthorized(
                "The posting must name the operator who booked it, and this session does not.");
        }

        var today = DateOnly.FromDateTime(SystemClock.UtcNow);
        var command = BookUnappliedReceiptsCommand.ForMonth(request?.From, request?.To, userId, today);

        var result = await _bookUnapplied.HandleAsync(command, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Posts a journal entry a person composed: a reclassification, an accrual, an opening balance
    /// (planning #795).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The only write in the ledger that names its accounts. Everything else posts through
    /// <c>ledger_mapping_rules</c>; a human adjustment is the case where the accounts <em>are</em> the
    /// decision, and the alternative to this endpoint was hand-written SQL that skips the numbering,
    /// the period check and the tamper hash.
    /// </para>
    /// <para>
    /// The operator comes from the session, never the body: a posting nobody can be asked about is
    /// not a posting this ledger takes. Idempotent on <c>reference</c> - a retry returns the entry the
    /// first call wrote instead of posting the same adjustment twice.
    /// </para>
    /// </remarks>
    [HttpPost("entries")]
    [Authorize(Policy = AuthPolicies.MoneyMovement)]
    [ProducesResponseType(typeof(PostManualJournalEntryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> PostManualEntry(
        [FromBody] PostManualJournalEntryRequest? request,
        CancellationToken cancellationToken)
    {
        if (CurrentUserId() is not { } userId)
        {
            return ApiError.Unauthorized(
                "The posting must name the operator who stands behind it, and this session does not.");
        }

        if (request is null)
        {
            return ApiError.BadRequest(
                "A manual entry needs a body: a date, a reference, a description and balanced legs.");
        }

        // LedgerPostingException and LedgerPeriodClosedException are the ledger's deliberate
        // validation channel and carry sentences written for the caller. The global handler maps
        // neither, so without this they would surface as a 500 - the one status that says "our
        // fault" about a request the caller can fix.
        try
        {
            var entry = await _manualEntries.HandleAsync(request, userId, cancellationToken);
            return Ok(entry);
        }
        catch (LedgerPeriodClosedException exception)
        {
            return ApiError.BadRequest(exception.Message, LedgerPeriodClosedException.Code);
        }
        catch (LedgerPostingException exception)
        {
            return ApiError.BadRequest(exception.Message, LedgerPostingException.Code);
        }
    }

    private Guid? CurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}

public sealed record BookUnappliedReceiptsRequest(DateOnly? From, DateOnly? To);

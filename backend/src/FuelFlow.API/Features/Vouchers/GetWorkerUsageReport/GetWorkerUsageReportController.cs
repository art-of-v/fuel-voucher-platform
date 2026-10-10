using System.Security.Claims;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.GetWorkerUsageReport;

/// <summary>
/// A worker's fuel usage for one company (multi-company epic #103, S5 - #150).
/// </summary>
/// <remarks>
/// The counterpart to the owner's view: a worker redeems but never buys, so what they need is not
/// stock or revenue but "what was I given, what have I used, and what is left".
/// </remarks>
[ApiController]
[Route("api/company/worker-usage")]
[Authorize]
public sealed class GetWorkerUsageReportController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public GetWorkerUsageReportController(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Received / used / remaining for every voucher a company has issued to the caller.
    /// </summary>
    /// <remarks>
    /// Scoped to the caller: a worker sees only their own vouchers, and only those issued by the
    /// company asked for. There is no way to read somebody else's.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetWorkerUsageReportResponse>> Get(
        [FromQuery] Guid legalEntityId,
        CancellationToken cancellationToken)
    {
        if (legalEntityId == Guid.Empty)
        {
            return BadRequest("legalEntityId is required");
        }

        var userId = GetUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        // The same predicate the wallet already uses to decide what a worker holds
        // (GetUserVouchersCommandHandler): issued to this worker, for this company.
        var rows = await _context.FuelVouchers
            .AsNoTracking()
            .Where(v => v.WorkerUserId == userId && v.LegalEntityId == legalEntityId)
            .Where(v => !v.IsDeleted && v.Status != VoucherStatus.VerificationFailed)
            .OrderByDescending(v => v.IssuedAtUtc)
            .ThenByDescending(v => v.CreatedAtUtc)
            .Select(v => new WorkerUsageListItem(
                v.Id,
                v.Provider,
                v.FuelTypeId,
                v.VoucherNumber,
                v.Liters,
                v.Status.ToString(),
                v.CustomerExpirationDate,
                v.IssuedAtUtc,
                v.UsedAtUtc,
                v.Status == VoucherStatus.Used))
            .ToListAsync(cancellationToken);

        // Totals are computed from the rows that were just returned, so the summary and the breakdown
        // cannot disagree - one list, one aggregate.
        var totals = new WorkerUsageTotals(
            Count: rows.Count,
            LitersReceived: rows.Sum(r => r.Liters),
            LitersUsed: rows.Where(r => r.IsUsed).Sum(r => r.Liters),
            LitersRemaining: rows.Where(r => !r.IsUsed).Sum(r => r.Liters),
            CountUsed: rows.Count(r => r.IsUsed),
            CountRemaining: rows.Count(r => !r.IsUsed));

        return Ok(new GetWorkerUsageReportResponse(legalEntityId, totals, rows));
    }

    /// <summary>
    /// The caller's user id. An authenticated request without a usable id claim is treated as
    /// unauthenticated rather than answered with somebody else's data.
    /// </summary>
    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return claim is not null && Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }
}

/// <summary>One voucher in the worker's report.</summary>
/// <param name="IssuedAtUtc">When the owner handed it over; null for a voucher issued before this
/// feature existed, since the column is only populated from now on.</param>
/// <param name="UsedAtUtc">When it was refuelled with; null while unused.</param>
public sealed record WorkerUsageListItem(
    Guid Id,
    string Provider,
    string FuelTypeId,
    string VoucherNumber,
    decimal Liters,
    string Status,
    DateOnly ExpirationDate,
    DateTime? IssuedAtUtc,
    DateTime? UsedAtUtc,
    bool IsUsed);

public sealed record WorkerUsageTotals(
    int Count,
    decimal LitersReceived,
    decimal LitersUsed,
    decimal LitersRemaining,
    int CountUsed,
    int CountRemaining);

public sealed record GetWorkerUsageReportResponse(
    Guid LegalEntityId,
    WorkerUsageTotals Totals,
    List<WorkerUsageListItem> Items);
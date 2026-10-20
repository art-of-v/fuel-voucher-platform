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
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (legalEntityId == Guid.Empty)
        {
            return BadRequest("legalEntityId is required");
        }

        // The page size is clamped by PagedRequest, so a client asking for
        // "?pageSize=2000000000" gets the maximum and not the whole table.
        var paging = new PagedRequest(Page: page, PageSize: pageSize);

        var userId = GetUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        // The same predicate the wallet already uses to decide what a worker holds
        // (GetUserVouchersCommandHandler): issued to this worker, for this company.
        var held = _context.FuelVouchers
            .AsNoTracking()
            .Where(v => v.WorkerUserId == userId && v.LegalEntityId == legalEntityId)
            .Where(v => !v.IsDeleted && v.Status != VoucherStatus.VerificationFailed);

        // The count is asked of the same query the page is, so the two cannot disagree.
        var totalCount = await held.CountAsync(cancellationToken);

        // The totals are computed over EVERY voucher the worker holds, not over the page. They
        // used to be computed from the rows the endpoint returned, which was the same number only
        // because the endpoint returned every row. A total that described the page would be a
        // different number on every page, and the summary is the part the worker reads.
        var totals = await held
            .GroupBy(_ => 1)
            .Select(g => new WorkerUsageTotals(
                Count: g.Count(),
                LitersReceived: g.Sum(v => v.Liters),
                LitersUsed: g.Where(v => v.Status == VoucherStatus.Used).Sum(v => v.Liters),
                LitersRemaining: g.Where(v => v.Status != VoucherStatus.Used).Sum(v => v.Liters),
                CountUsed: g.Count(v => v.Status == VoucherStatus.Used),
                CountRemaining: g.Count(v => v.Status != VoucherStatus.Used)))
            .FirstOrDefaultAsync(cancellationToken)
            ?? new WorkerUsageTotals(0, 0, 0, 0, 0, 0);

        var rows = await held
            .OrderByDescending(v => v.IssuedAtUtc)
            .ThenByDescending(v => v.CreatedAtUtc)
            // The tie-break: two vouchers can share an issue timestamp, and a page boundary
            // that falls between them would skip one. A skipped row is fuel the worker does
            // not know they hold.
            .ThenBy(v => v.Id)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(v => new WorkerUsageListItem(
                v.Id,
                v.Brand,
                v.FuelTypeId,
                v.VoucherNumber,
                v.Liters,
                v.Status.ToString(),
                v.CustomerExpirationDate,
                v.IssuedAtUtc,
                v.UsedAtUtc,
                v.Status == VoucherStatus.Used))
            .ToListAsync(cancellationToken);

        // The body keeps its shape - the app reads an object with a totals block and an items
        // list - and the count travels in a header, so a client can tell that another page
        // follows without the shape changing.
        Response.Headers["X-Total-Count"] = totalCount.ToString();
        Response.Headers["X-Has-Next-Page"] = totalCount > paging.Skip + rows.Count ? "true" : "false";

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
    string Brand,
    Guid FuelTypeId,
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
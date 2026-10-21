using FuelFlow.Features.Acquiring.Reconciliation;
using FuelFlow.SharedKernel.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.API.Features.Acquiring;

/// <summary>
/// The reconciliation act for a period (#118): the acquirer's statement against our own record.
/// </summary>
/// <remarks>
/// Read-only, and separate from the existing <c>GET /api/admin/reconciliation</c> on purpose. That
/// one reconciles orders against vouchers and margin - all internal, all ours. This one compares two
/// independent sources, which is the only reconciliation that can find us being wrong.
///
/// <para>
/// One of the two sources cannot report a reversal on its own: Monobank nests a reversal under the
/// payment it reverses and files no row for it, so the acquirer's statement alone cannot show money
/// going back. The act therefore reconciles the refunds from our side - the refunds we issued in
/// the period - beside the statement, with a figure of their own. See <c>StatementRefunds</c>.
/// </para>
/// </remarks>
[ApiController]
[Route("api/admin/reconciliation/statement")]
[Authorize(Policy = "Staff")]
public sealed class StatementReconciliationController : ControllerBase
{
    private readonly GetStatementReconciliationQueryHandler _handler;

    public StatementReconciliationController(GetStatementReconciliationQueryHandler handler)
    {
        _handler = handler;
    }

    /// <summary>The act as JSON: the summary, every row with its verdict, the orders the
    /// acquirer never mentioned, and the refunds we issued in the period.</summary>
    // Route restored 2026-10-12. Commenting out [HttpGet] does not disable an action: under
    // [ApiController] a method with no HTTP attribute binds to the controller prefix, so all three
    // actions here claimed `api/admin/reconciliation/statement` and the router answered 500
    // (AmbiguousMatchException) instead of 404 - taking the whole act offline in production.
    // It has a caller now anyway: the admin's statement panel, which was unreachable without this.
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        [FromQuery] MonobankMerchant? merchant,
        [FromQuery] int? year,
        [FromQuery] int? month,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        CancellationToken cancellationToken)
    {
        var act = await _handler.HandleAsync(
            new StatementReconciliationQuery
            {
                Merchant = merchant,
                Year = year,
                Month = month,
                FromUtc = fromUtc,
                ToUtc = toUtc
            },
            cancellationToken);

        return Ok(act);
    }

    /// <summary>The act as CSV, for a spreadsheet.</summary>
    /// <remarks>
    /// The download filename is asserted separately: the media type is declared on the 200 only,
    /// because <c>[Produces]</c> applies content negotiation to every return and an empty 404 has no
    /// CSV formatter to negotiate with - which turns "no such period" into a 406.
    /// </remarks>
    [HttpGet("csv")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "text/csv")]
    public async Task<IActionResult> GetCsv(
        [FromQuery] MonobankMerchant? merchant,
        [FromQuery] int? year,
        [FromQuery] int? month,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        CancellationToken cancellationToken)
    {
        var act = await _handler.HandleAsync(
            new StatementReconciliationQuery { Merchant = merchant, Year = year, Month = month, FromUtc = fromUtc, ToUtc = toUtc },
            cancellationToken);

        return File(StatementReconciliationCsv.RenderBytes(act), "text/csv", FileName(act, "csv"));
    }

    /// <summary>The act as PDF - the form it gets signed off in.</summary>
    [HttpGet("pdf")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "application/pdf")]
    public async Task<IActionResult> GetPdf(
        [FromQuery] MonobankMerchant? merchant,
        [FromQuery] int? year,
        [FromQuery] int? month,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        CancellationToken cancellationToken)
    {
        var act = await _handler.HandleAsync(
            new StatementReconciliationQuery { Merchant = merchant, Year = year, Month = month, FromUtc = fromUtc, ToUtc = toUtc },
            cancellationToken);

        return File(StatementReconciliationPdf.Build(act), "application/pdf", FileName(act, "pdf"));
    }

    /// <summary>
    /// Names the file after the period it covers, not the moment it was requested - so "October"
    /// stays October however late someone remembers to export it.
    /// </summary>
    private static string FileName(StatementReconciliationResponse act, string extension)
        => FormattableString.Invariant(
            $"reconciliation-{act.Merchant.ToString().ToLowerInvariant()}-{act.FromUtc:yyyy-MM}-{extension}");
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Vouchers.Exchange;

/// <summary>
/// Admin-only operator→supplier voucher exchange / renewal (planning #104). The operator renews our
/// own lapsing STOCK vouchers with the supplier off-platform (no payment, no device signature) and
/// records it here: which old stock → which new stock, the доплата, and the cost that reprices the
/// remaining stock. See <see cref="ConfirmVoucherExchangeCommandHandler"/>.
/// </summary>
[ApiController]
[Route("api/admin/voucher-exchange")]
[Authorize(Policy = "Staff")]
public sealed class VoucherExchangeController : ControllerBase
{
    private readonly GetVoucherExchangeAttentionQueryHandler _attentionHandler;
    private readonly GetVoucherExchangeCostContextQueryHandler _costContextHandler;
    private readonly GetVoucherExchangeBatchesQueryHandler _batchesHandler;
    private readonly GetVoucherExchangeBatchQueryHandler _batchHandler;
    private readonly ConfirmVoucherExchangeCommandHandler _confirmHandler;

    public VoucherExchangeController(
        GetVoucherExchangeAttentionQueryHandler attentionHandler,
        GetVoucherExchangeCostContextQueryHandler costContextHandler,
        GetVoucherExchangeBatchesQueryHandler batchesHandler,
        GetVoucherExchangeBatchQueryHandler batchHandler,
        ConfirmVoucherExchangeCommandHandler confirmHandler)
    {
        _attentionHandler = attentionHandler;
        _costContextHandler = costContextHandler;
        _batchesHandler = batchesHandler;
        _batchHandler = batchHandler;
        _confirmHandler = confirmHandler;
    }

    /// <summary>The recorded exchanges, newest first. The audit read the operator had no way to do.</summary>
    [HttpGet("batches")]
    public async Task<IActionResult> GetBatches(
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20,
        [FromQuery] Guid? supplierId = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null)
    {
        var query = new GetVoucherExchangeBatchesQuery(page, limit, supplierId, from, to);
        return Ok(await _batchesHandler.HandleAsync(query, cancellationToken));
    }

    /// <summary>One exchange batch, pair by pair: every retired voucher, its replacement, and its own cost.</summary>
    [HttpGet("batches/{exchangeBatchId:guid}")]
    public async Task<IActionResult> GetBatch(Guid exchangeBatchId, CancellationToken cancellationToken)
    {
        var result = await _batchHandler.HandleAsync(
            new GetVoucherExchangeBatchQuery(exchangeBatchId), cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>Stock vouchers already expired or lapsing within the renewal threshold.</summary>
    [HttpGet("attention")]
    public async Task<IActionResult> GetAttention(CancellationToken cancellationToken)
        => Ok(await _attentionHandler.HandleAsync(cancellationToken));

    /// <summary>Cheap count behind the nav badge.</summary>
    [HttpGet("attention/count")]
    public async Task<IActionResult> GetAttentionCount(CancellationToken cancellationToken)
        => Ok(new { count = await _attentionHandler.CountAsync(cancellationToken) });

    /// <summary>Per-fuel cost context for a freshly-imported batch, to pre-fill the exchange cost/liter.</summary>
    [HttpGet("cost-context")]
    public async Task<IActionResult> GetCostContext([FromQuery] Guid importId, CancellationToken cancellationToken)
    {
        if (importId == Guid.Empty)
            return BadRequest(new { error = "importId is required." });
        return Ok(await _costContextHandler.HandleAsync(importId, cancellationToken));
    }

    /// <summary>Confirm the exchange: expire the olds, cost + activate the news, pair, and audit.</summary>
    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm([FromBody] ConfirmVoucherExchangeRequest request, CancellationToken cancellationToken)
    {
        // Refused rather than performed anonymously. An exchange is settlement: it retires vouchers,
        // reprices stock and writes both a movement and a status event, all attributed to this id.
        // With no id there is nothing to attribute it to, and the writers can only skip the record -
        // which would leave the settlement done and unexplained.
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var command = new ConfirmVoucherExchangeCommand(
            request.OldVoucherIds ?? new List<Guid>(),
            request.NewImportId,
            (request.Costs ?? new List<ExchangeFuelCostDto>())
                .Select(c => new ExchangeFuelCost(c.FuelTypeId, c.CostPerLiter))
                .ToList(),
            request.SurchargeUah,
            userId.Value,
            GetUserName());

        var result = await _confirmHandler.HandleAsync(command, cancellationToken);
        return result.Success ? Ok(result) : BadRequest(new { error = result.Error, result });
    }

    /// <summary>
    /// The authenticated operator's id, or <c>null</c> when the token carries none.
    /// </summary>
    /// <remarks>
    /// Was <see cref="Guid.Empty"/>, which is NOT NULL and therefore satisfied
    /// <c>ck_voucher_events_actor_named</c> on its own: an operator action with no authenticated actor
    /// was recorded as <em>this user did it, and here is their id</em>, naming a user who does not
    /// exist. That is the one author the actor CHECKs cannot catch, which is why the exchange writer
    /// also tests for it — this removes the possibility rather than the symptom. Every token
    /// <c>JwtTokenService</c> mints carries <c>sub</c>, so null here means a token this service did
    /// not issue.
    /// </remarks>
    private Guid? GetUserId()
    {
        var claim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return claim is not null && Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    private string? GetUserName()
    {
        var first = User.FindFirst("first_name")?.Value;
        var last = User.FindFirst("last_name")?.Value;
        if (first is not null && last is not null) return $"{first} {last}";
        if (first is not null) return first;
        if (last is not null) return last;
        return User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
    }
}

public sealed class ConfirmVoucherExchangeRequest
{
    public List<Guid>? OldVoucherIds { get; set; }
    public Guid NewImportId { get; set; }
    public List<ExchangeFuelCostDto>? Costs { get; set; }
    public decimal SurchargeUah { get; set; }
}

public sealed class ExchangeFuelCostDto
{
    public Guid FuelTypeId { get; set; }
    public decimal CostPerLiter { get; set; }
}

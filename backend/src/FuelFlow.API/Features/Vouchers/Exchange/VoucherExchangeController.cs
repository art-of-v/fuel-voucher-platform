using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Vouchers.Exchange;

/// <summary>
/// Admin-only operator→provider voucher exchange / renewal (planning #104). The operator renews our
/// own lapsing STOCK vouchers with the provider off-platform (no payment, no device signature) and
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
    private readonly ConfirmVoucherExchangeCommandHandler _confirmHandler;

    public VoucherExchangeController(
        GetVoucherExchangeAttentionQueryHandler attentionHandler,
        GetVoucherExchangeCostContextQueryHandler costContextHandler,
        ConfirmVoucherExchangeCommandHandler confirmHandler)
    {
        _attentionHandler = attentionHandler;
        _costContextHandler = costContextHandler;
        _confirmHandler = confirmHandler;
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
        var command = new ConfirmVoucherExchangeCommand(
            request.OldVoucherIds ?? new List<Guid>(),
            request.NewImportId,
            (request.Costs ?? new List<ExchangeFuelCostDto>())
                .Select(c => new ExchangeFuelCost(c.FuelTypeId, c.CostPerLiter))
                .ToList(),
            request.SurchargeUah,
            request.InvoiceNumber,
            request.InvoiceDate,
            GetUserId(),
            GetUserName());

        var result = await _confirmHandler.HandleAsync(command, cancellationToken);
        return result.Success ? Ok(result) : BadRequest(new { error = result.Error, result });
    }

    private Guid GetUserId()
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
    public string? InvoiceNumber { get; set; }
    public DateOnly? InvoiceDate { get; set; }
}

public sealed class ExchangeFuelCostDto
{
    public string FuelTypeId { get; set; } = string.Empty;
    public decimal CostPerLiter { get; set; }
}

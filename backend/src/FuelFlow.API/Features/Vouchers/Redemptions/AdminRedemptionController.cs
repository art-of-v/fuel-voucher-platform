using System.Security.Claims;
using FuelFlow.Features.Vouchers.Redemptions.DecideRedemption;
using FuelFlow.Features.Vouchers.Redemptions.DismissStatementRow;
using FuelFlow.Features.Vouchers.Redemptions.GetExceptions;
using FuelFlow.Features.Vouchers.Redemptions.ImportStatement;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Vouchers.Redemptions;

[ApiController]
[Route("api/admin/redemptions")]
public sealed class AdminRedemptionController : ControllerBase
{
    private readonly ImportRedemptionStatementCommandHandler _importHandler;
    private readonly GetRedemptionExceptionsQueryHandler _exceptionsHandler;
    private readonly DecideRedemptionCommandHandler _decisionHandler;
    private readonly DismissRedemptionStatementRowCommandHandler _dismissHandler;

    public AdminRedemptionController(
        ImportRedemptionStatementCommandHandler importHandler,
        GetRedemptionExceptionsQueryHandler exceptionsHandler,
        DecideRedemptionCommandHandler decisionHandler,
        DismissRedemptionStatementRowCommandHandler dismissHandler)
    {
        _importHandler = importHandler;
        _exceptionsHandler = exceptionsHandler;
        _decisionHandler = decisionHandler;
        _dismissHandler = dismissHandler;
    }

    /// <summary>
    /// Imports one supplier or pump statement. Re-importing the same line is a no-op, keyed on
    /// <see cref="ImportRedemptionStatementLineRequest.ExternalId"/>.
    /// </summary>
    [HttpPost("statement")]
    [Authorize(Policy = AuthPolicies.VoucherWrite)]
    [ProducesResponseType(typeof(ImportRedemptionStatementResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ImportStatement(
        [FromBody] ImportRedemptionStatementRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null) return ApiError.Unauthorized("Unauthorized");

        var lines = (request.Lines ?? [])
            .Select(l => new ImportRedemptionStatementLine(
                l.ExternalId ?? string.Empty,
                l.VoucherNumber ?? string.Empty,
                l.Liters,
                l.RedeemedAtUtc,
                l.Note))
            .ToList();

        var response = await _importHandler.HandleAsync(
            new ImportRedemptionStatementCommand(userId.Value, lines),
            cancellationToken);

        if (!response.Success)
        {
            return ApiError.BadRequest(response.Error ?? "The statement could not be imported.");
        }

        return Ok(response);
    }

    /// <summary>
    /// Everything waiting for a human: live claims, the claims whose statement says different
    /// litres, and statement lines that matched no live claim.
    /// </summary>
    [HttpGet("exceptions")]
    [Authorize(Policy = AuthPolicies.Staff)]
    [ProducesResponseType(typeof(GetRedemptionExceptionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetExceptions(CancellationToken cancellationToken)
    {
        var response = await _exceptionsHandler.HandleAsync(
            new GetRedemptionExceptionsQuery(),
            cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Accepts, rejects or reverses the claim on one voucher. Confirm posts the provisional sale;
    /// reject discards it and returns the voucher to Assigned; reverse is only for a confirmed,
    /// posted redemption in an open period.
    /// </summary>
    [HttpPost("{voucherId:guid}/decision")]
    [Authorize(Policy = AuthPolicies.VoucherWrite)]
    [ProducesResponseType(typeof(DecideRedemptionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Decide(
        Guid voucherId,
        [FromBody] RedemptionDecisionRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null) return ApiError.Unauthorized("Unauthorized");

        if (!Enum.TryParse<RedemptionDecisionAction>(request.Action, ignoreCase: true, out var action)
            || !Enum.IsDefined(action))
        {
            return ApiError.BadRequest("Action must be Confirm, Reject or Reverse.");
        }

        var response = await _decisionHandler.HandleAsync(
            new DecideRedemptionCommand(voucherId, action, userId.Value, request.Note),
            cancellationToken);

        if (response.Success)
        {
            return Ok(response);
        }

        return response.ErrorCode == "NotFound"
            ? ApiError.NotFound(response.Message)
            : ApiError.BadRequest(response.Message);
    }

    /// <summary>
    /// Closes a statement line that needs no voucher action. A line attached to a live claim is
    /// refused: confirm or reject that claim instead.
    /// </summary>
    [HttpPost("statement/{rowId:guid}/dismiss")]
    [Authorize(Policy = AuthPolicies.VoucherWrite)]
    [ProducesResponseType(typeof(DismissRedemptionStatementRowResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DismissStatementLine(
        Guid rowId,
        [FromBody] DismissRedemptionStatementRowRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null) return ApiError.Unauthorized("Unauthorized");

        var response = await _dismissHandler.HandleAsync(
            new DismissRedemptionStatementRowCommand(rowId, userId.Value, request.Note),
            cancellationToken);

        if (response.Success)
        {
            return Ok(response);
        }

        return response.ErrorCode == "NotFound"
            ? ApiError.NotFound(response.Message)
            : ApiError.BadRequest(response.Message);
    }

    private Guid? GetUserId()
    {
        var value = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? User.FindFirst("sub")?.Value;

        return Guid.TryParse(value, out var id) ? id : null;
    }
}

public sealed record ImportRedemptionStatementRequest(
    IReadOnlyList<ImportRedemptionStatementLineRequest>? Lines);

public sealed record ImportRedemptionStatementLineRequest(
    string? ExternalId,
    string? VoucherNumber,
    decimal Liters,
    DateTime? RedeemedAtUtc,
    string? Note);

public sealed record RedemptionDecisionRequest(string? Action, string? Note);

public sealed record DismissRedemptionStatementRowRequest(string? Note);

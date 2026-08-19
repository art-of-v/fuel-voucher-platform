using FuelFlow.Features.Vouchers.BulkActionVouchers;
using FuelFlow.Features.Vouchers.DeleteVoucher;
using FuelFlow.Features.Vouchers.GetAdminVoucherById;
using FuelFlow.Features.Vouchers.GetAdminVouchers;
using FuelFlow.Features.Vouchers.GetVoucherVerification;
using FuelFlow.Features.Vouchers.UnblockVoucher;
using FuelFlow.Features.Vouchers.UpdateVoucher;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Vouchers;

[ApiController]
[Route("api/admin/vouchers")]
[Authorize(Roles = "Admin")]
public sealed class AdminVoucherController : ControllerBase
{
    private readonly GetAdminVouchersQueryHandler _getAllHandler;
    private readonly GetAdminVoucherByIdQueryHandler _getByIdHandler;
    private readonly GetVoucherVerificationQueryHandler _getVerificationHandler;
    private readonly UpdateVoucherCommandHandler _updateHandler;
    private readonly DeleteVoucherCommandHandler _deleteHandler;
    private readonly BulkActionVouchersCommandHandler _bulkActionHandler;
    private readonly UnblockVoucherCommandHandler _unblockHandler;

    public AdminVoucherController(
        GetAdminVouchersQueryHandler getAllHandler,
        GetAdminVoucherByIdQueryHandler getByIdHandler,
        GetVoucherVerificationQueryHandler getVerificationHandler,
        UpdateVoucherCommandHandler updateHandler,
        DeleteVoucherCommandHandler deleteHandler,
        BulkActionVouchersCommandHandler bulkActionHandler,
        UnblockVoucherCommandHandler unblockHandler)
    {
        _getAllHandler = getAllHandler;
        _getByIdHandler = getByIdHandler;
        _getVerificationHandler = getVerificationHandler;
        _updateHandler = updateHandler;
        _deleteHandler = deleteHandler;
        _bulkActionHandler = bulkActionHandler;
        _unblockHandler = unblockHandler;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 50,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDirection = null,
        [FromQuery] string? fuelType = null,
        [FromQuery] string? status = null,
        [FromQuery] string? provider = null,
        [FromQuery] string? amount = null,
        [FromQuery] string? expirationDate = null,
        [FromQuery] Guid? workerUserId = null)
    {
        var query = new GetAdminVouchersQuery(
            page, limit, sortBy, sortDirection,
            fuelType, status, provider, amount, expirationDate, workerUserId);

        var result = await _getAllHandler.HandleAsync(query, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var result = await _getByIdHandler.HandleAsync(new GetAdminVoucherByIdQuery(id), cancellationToken);
        if (result is null) return NotFound();
        return Ok(result);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateVoucherRequest request, CancellationToken cancellationToken)
    {
        var result = await _updateHandler.HandleAsync(
            new UpdateVoucherCommand(id, request.Status, request.AssignedToUserId, GetUserId(), GetUserName()), cancellationToken);
        if (result is null) return NotFound();
        if (!result.Success && !string.IsNullOrWhiteSpace(result.Error))
            return BadRequest(new { error = result.Error });
        return Ok(new { success = true });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var result = await _deleteHandler.HandleAsync(
            new DeleteVoucherCommand(id, GetUserId(), GetUserName()), cancellationToken);
        if (result is null) return NotFound();
        return Ok(new { success = true });
    }

    [HttpPost("bulk-action")]
    public async Task<IActionResult> BulkAction([FromBody] BulkActionRequest request, CancellationToken cancellationToken)
    {
        var result = await _bulkActionHandler.HandleAsync(
            new BulkActionVouchersCommand(request.Action, request.Ids, request.TargetUserId, GetUserId(), GetUserName()), cancellationToken);

        if (!result.Success && !string.IsNullOrWhiteSpace(result.Error))
            return BadRequest(new { error = result.Error });

        return Ok(new { success = result.Success, count = result.Count });
    }

    [HttpPost("{id:guid}/unblock")]
    public async Task<IActionResult> Unblock(Guid id, CancellationToken cancellationToken)
    {
        var result = await _unblockHandler.HandleAsync(
            new UnblockVoucherCommand(id, GetUserId(), GetUserName()),
            cancellationToken);

        if (result is null) return NotFound();
        if (!result.Success && !string.IsNullOrWhiteSpace(result.Error))
            return BadRequest(new { error = result.Error });

        return Ok(new { success = true });
    }

    [HttpGet("{id:guid}/verification")]
    [ProducesResponseType(typeof(GetVoucherVerificationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVoucherVerification(Guid id, CancellationToken cancellationToken)
    {
        var result = await _getVerificationHandler.HandleAsync(new GetVoucherVerificationQuery(id), cancellationToken);
        if (result == null)
            return NotFound();

        return Ok(result);
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

public sealed class UpdateVoucherRequest
{
    public string? Status { get; set; }
    public Guid? AssignedToUserId { get; set; }
}

public sealed class BulkActionRequest
{
    public string Action { get; set; } = null!;
    public List<Guid>? Ids { get; set; }
    public Guid? TargetUserId { get; set; }
}

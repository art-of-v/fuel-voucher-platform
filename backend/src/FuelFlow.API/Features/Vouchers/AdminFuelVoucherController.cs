using FuelFlow.Features.Vouchers.DeleteVoucher;
using FuelFlow.Features.Vouchers.GetAdminVoucherById;
using FuelFlow.Features.Vouchers.GetFuelVouchers;
using FuelFlow.Features.Vouchers.UpdateVoucher;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Vouchers;

[ApiController]
[Route("api/admin/fuel-vouchers")]
[Authorize(Roles = "Admin")]
public sealed class AdminFuelVoucherController : ControllerBase
{
    private readonly GetFuelVouchersQueryHandler _getAllHandler;
    private readonly GetAdminVoucherByIdQueryHandler _getByIdHandler;
    private readonly UpdateVoucherCommandHandler _updateHandler;
    private readonly DeleteVoucherCommandHandler _deleteHandler;

    public AdminFuelVoucherController(
        GetFuelVouchersQueryHandler getAllHandler,
        GetAdminVoucherByIdQueryHandler getByIdHandler,
        UpdateVoucherCommandHandler updateHandler,
        DeleteVoucherCommandHandler deleteHandler)
    {
        _getAllHandler = getAllHandler;
        _getByIdHandler = getByIdHandler;
        _updateHandler = updateHandler;
        _deleteHandler = deleteHandler;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        CancellationToken cancellationToken,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var result = await _getAllHandler.HandleAsync(
            new GetFuelVouchersQuery(search, status, page, pageSize), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var result = await _getByIdHandler.HandleAsync(new GetAdminVoucherByIdQuery(id), cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateFuelVoucherRequest request, CancellationToken cancellationToken)
    {
        var result = await _updateHandler.HandleAsync(
            new UpdateVoucherCommand(id, request.Status, request.AssignedToUserId), cancellationToken);
        if (result is null) return NotFound();
        return Ok(new { success = true });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var result = await _deleteHandler.HandleAsync(new DeleteVoucherCommand(id), cancellationToken);
        if (result is null) return NotFound();
        return Ok(new { success = true });
    }
}

public sealed class UpdateFuelVoucherRequest
{
    public string? Status { get; set; }
    public Guid? AssignedToUserId { get; set; }
}

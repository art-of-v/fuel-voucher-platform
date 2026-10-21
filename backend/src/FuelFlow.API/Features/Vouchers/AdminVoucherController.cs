using System.Security.Claims;
using FuelFlow.Features.Vouchers.BulkActionVouchers;
using FuelFlow.Features.Vouchers.DeleteVoucher;
using FuelFlow.Features.Vouchers.GetAdminVoucherById;
using FuelFlow.Features.Vouchers.GetAdminVoucherLifecycle;
using FuelFlow.Features.Vouchers.GetAdminVouchers;
using FuelFlow.Features.Vouchers.GetVoucherVerification;
using FuelFlow.Features.Vouchers.UnblockVoucher;
using FuelFlow.Features.Vouchers.UnarchiveVoucher;
using FuelFlow.Features.Vouchers.UpdateVoucher;
using FuelFlow.SharedKernel.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Vouchers;

[ApiController]
[Route("api/admin/vouchers")]
public sealed class AdminVoucherController : ControllerBase
{
    private readonly GetAdminVouchersQueryHandler _getAllHandler;
    private readonly GetAdminVoucherByIdQueryHandler _getByIdHandler;
    private readonly GetVoucherVerificationQueryHandler _getVerificationHandler;
    private readonly UnblockVoucherCommandHandler _unblockHandler;
    private readonly UpdateVoucherCommandHandler _updateHandler;
    private readonly DeleteVoucherCommandHandler _deleteHandler;
    private readonly BulkActionVouchersCommandHandler _bulkActionHandler;
    private readonly UnarchiveVoucherCommandHandler _unarchiveHandler;
    private readonly GetAdminVoucherLifecycleQueryHandler _lifecycleHandler;

    public AdminVoucherController(
        GetAdminVouchersQueryHandler getAllHandler,
        GetAdminVoucherByIdQueryHandler getByIdHandler,
        GetVoucherVerificationQueryHandler getVerificationHandler,
        UnblockVoucherCommandHandler unblockHandler,
        UpdateVoucherCommandHandler updateHandler,
        DeleteVoucherCommandHandler deleteHandler,
        BulkActionVouchersCommandHandler bulkActionHandler,
        UnarchiveVoucherCommandHandler unarchiveHandler,
        GetAdminVoucherLifecycleQueryHandler lifecycleHandler)
    {
        _getAllHandler = getAllHandler;
        _getByIdHandler = getByIdHandler;
        _getVerificationHandler = getVerificationHandler;
        _unblockHandler = unblockHandler;
        _updateHandler = updateHandler;
        _deleteHandler = deleteHandler;
        _bulkActionHandler = bulkActionHandler;
        _unarchiveHandler = unarchiveHandler;
    }

    [HttpGet]
    [Authorize(Policy = AuthPolicies.Staff)]
    public async Task<IActionResult> GetAll(
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 50,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDirection = null,
        [FromQuery] string? fuelType = null,
        [FromQuery] Guid? fuelTypeId = null,
        [FromQuery] string? status = null,
        [FromQuery] string? brand = null,
        [FromQuery] string? amount = null,
        [FromQuery] string? expirationDate = null,
        [FromQuery] Guid? workerUserId = null)
    {
        var query = new GetAdminVouchersQuery(
            page, limit, sortBy, sortDirection,
            fuelType, fuelTypeId, status, brand, amount, expirationDate, workerUserId);

        var result = await _getAllHandler.HandleAsync(query, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id}")]
    [Authorize(Policy = AuthPolicies.Staff)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var result = await _getByIdHandler.HandleAsync(new GetAdminVoucherByIdQuery(id), cancellationToken);
        if (result is null) return NotFound();
        return Ok(result);
    }

    [HttpPut("{id}")]
    [Authorize(Policy = AuthPolicies.VoucherWrite)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateVoucherRequest request, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var result = await _updateHandler.HandleAsync(
            new UpdateVoucherCommand(id, request.Status, request.AssignedToUserId, userId, GetUserName()), cancellationToken);
        if (result is null) return NotFound();
        if (!result.Success && !string.IsNullOrWhiteSpace(result.Error))
            return BadRequest(new { error = result.Error });
        return Ok(new { success = true });
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = AuthPolicies.VoucherWrite)]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var result = await _deleteHandler.HandleAsync(
            new DeleteVoucherCommand(id, userId, GetUserName()), cancellationToken);
        if (result is null) return NotFound();
        if (!result.Success) return BadRequest(new { error = result.Error });
        return Ok(new { success = true });
    }

    [HttpPost("bulk-action")]
    [Authorize(Policy = AuthPolicies.VoucherWrite)]
    public async Task<IActionResult> BulkAction([FromBody] BulkActionRequest request, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var result = await _bulkActionHandler.HandleAsync(
            new BulkActionVouchersCommand(request.Action, request.Ids, request.TargetUserId, userId, GetUserName()), cancellationToken);

        if (!result.Success && !string.IsNullOrWhiteSpace(result.Error))
            return BadRequest(new { error = result.Error });

        return Ok(new { success = result.Success, count = result.Count });
    }

    [HttpPost("{id:guid}/unblock")]
    [Authorize(Policy = AuthPolicies.VoucherWrite)]
    public async Task<IActionResult> Unblock(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var result = await _unblockHandler.HandleAsync(
            new UnblockVoucherCommand(id, userId, GetUserName()),
            cancellationToken);

        if (result is null) return NotFound();
        if (!result.Success && !string.IsNullOrWhiteSpace(result.Error))
            return BadRequest(new { error = result.Error });

        return Ok(new { success = true });
    }
    /// <summary>
    /// The undo for the soft delete. Deliberately a separate endpoint from the customer-facing
    /// <c>PATCH /api/vouchers/{id}/restore</c>, which undoes a *redemption* and keeps the
    /// <c>!IsDeleted</c> filter in force: un-archiving is an operator action, and
    /// <c>delete_all</c> archives every voucher in the system.
    /// </summary>
    [HttpPost("{id:guid}/unarchive")]
    [Authorize(Policy = AuthPolicies.VoucherWrite)]
    public async Task<IActionResult> Unarchive(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var result = await _unarchiveHandler.HandleAsync(
            new UnarchiveVoucherCommand(id, userId, GetUserName()),
            cancellationToken);

        if (result is null) return NotFound();
        if (!result.Success && !string.IsNullOrWhiteSpace(result.Error))
            return BadRequest(new { error = result.Error });

        return Ok(new { success = true });
    }

    [HttpGet("{id:guid}/verification")]
    [Authorize(Policy = AuthPolicies.Staff)]
    [ProducesResponseType(typeof(GetVoucherVerificationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVoucherVerification(Guid id, CancellationToken cancellationToken)
    {
        var result = await _getVerificationHandler.HandleAsync(new GetVoucherVerificationQuery(id), cancellationToken);
        if (result == null)
            return NotFound();

        return Ok(result);
    }

    /// <summary>
    /// One voucher, whole: the paper's custody, everything done to it, and the paid renewals in its
    /// lineage, in one time-ordered list.
    /// </summary>
    /// <remarks>
    /// The reader D-13 was built for. Nineteen writers record every handover and transition and until
    /// now nothing could open them, so "when did this voucher become sellable and who said so" had no
    /// answer for any voucher in the system.
    /// <para>
    /// An archived voucher returns 200 with <c>isArchived</c> set rather than 404: the archive flag is
    /// itself part of the record and has its own event in the timeline, and the one voucher an operator
    /// most wants to look up is the one somebody archived.
    /// </para>
    /// </remarks>
    [HttpGet("{id:guid}/lifecycle")]
    [Authorize(Policy = AuthPolicies.Staff)]
    [ProducesResponseType(typeof(VoucherLifecycleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLifecycle(Guid id, CancellationToken cancellationToken)
    {
        var result = await _lifecycleHandler.HandleAsync(new GetAdminVoucherLifecycleQuery(id), cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// The authenticated admin's id, or <c>null</c> when the token carries none.
    /// </summary>
    /// <remarks>
    /// This used to return <see cref="Guid.Empty"/> instead of <c>null</c>, and that value is NOT NULL —
    /// so it satisfied <c>ck_voucher_events_actor_named</c> and the audit row it fed was written as
    /// <em>this user did it, and here is their id</em>, naming a user who does not exist. It is the one
    /// way to record an author that the actor CHECKs cannot catch, which is why the writers now treat
    /// <see cref="Guid.Empty"/> as "no actor" and skip the event rather than store a plausible nobody.
    /// <para>
    /// Skipping is the safety net; this is the fix. Every token <c>JwtTokenService</c> mints carries
    /// <c>sub</c>, which JwtBearer's default claim mapping turns into
    /// <see cref="ClaimTypes.NameIdentifier"/>, so a null here means a token this service did not issue.
    /// A write that cannot be attributed is refused rather than performed anonymously.
    /// </para>
    /// </remarks>
    private Guid? GetUserId()
    {
        var value = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? User.FindFirst("sub")?.Value;

        return Guid.TryParse(value, out var id) ? id : null;
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

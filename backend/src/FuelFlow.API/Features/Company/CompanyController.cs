using System.Security.Claims;
using FuelFlow.Features.Company.AcceptInvitation;
using FuelFlow.Features.Company.BlockWorkerVoucher;
using FuelFlow.Features.Company.CancelInvitation;
using FuelFlow.Features.Company.DeclineInvitation;
using FuelFlow.Features.Company.FireWorker;
using FuelFlow.Features.Company.GetMembers;
using FuelFlow.Features.Company.GetMyInvitations;
using FuelFlow.Features.Company.GetMyMemberships;
using FuelFlow.Features.Company.GetOwnerInvitations;
using FuelFlow.Features.Company.GiftVouchers;
using FuelFlow.Features.Company.RecallVoucher;
using FuelFlow.Features.Company.SendInvitation;
using FuelFlow.Features.Company.UnblockWorkerVoucher;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using static FuelFlow.API.Extensions.RateLimiterSetup;

namespace FuelFlow.Features.Company;

[ApiController]
[Route("api/company")]
[Authorize]
public sealed class CompanyController : ControllerBase
{
    private readonly SendInvitationCommandHandler _sendInvitationHandler;
    private readonly GetOwnerInvitationsQueryHandler _getOwnerInvitationsHandler;
    private readonly CancelInvitationCommandHandler _cancelInvitationHandler;
private readonly GetMyInvitationsQueryHandler _getMyInvitationsHandler;
    private readonly GetMyMembershipsQueryHandler _getMyMembershipsHandler;
    private readonly AcceptInvitationCommandHandler _acceptInvitationHandler;
    private readonly DeclineInvitationCommandHandler _declineInvitationHandler;
    private readonly GetMembersQueryHandler _getMembersHandler;
    private readonly FireWorkerCommandHandler _fireWorkerHandler;
    private readonly GiftVouchersCommandHandler _giftVouchersHandler;
    private readonly RecallVoucherCommandHandler _recallVoucherHandler;
    private readonly BlockWorkerVoucherCommandHandler _blockWorkerVoucherHandler;
    private readonly UnblockWorkerVoucherCommandHandler _unblockWorkerVoucherHandler;

    public CompanyController(
        SendInvitationCommandHandler sendInvitationHandler,
        GetOwnerInvitationsQueryHandler getOwnerInvitationsHandler,
        CancelInvitationCommandHandler cancelInvitationHandler,
        GetMyInvitationsQueryHandler getMyInvitationsHandler,
        GetMyMembershipsQueryHandler getMyMembershipsHandler,
        AcceptInvitationCommandHandler acceptInvitationHandler,
        DeclineInvitationCommandHandler declineInvitationHandler,
        GetMembersQueryHandler getMembersHandler,
        FireWorkerCommandHandler fireWorkerHandler,
        GiftVouchersCommandHandler giftVouchersHandler,
        RecallVoucherCommandHandler recallVoucherHandler,
        BlockWorkerVoucherCommandHandler blockWorkerVoucherHandler,
        UnblockWorkerVoucherCommandHandler unblockWorkerVoucherHandler)
    {
        _sendInvitationHandler = sendInvitationHandler;
        _getOwnerInvitationsHandler = getOwnerInvitationsHandler;
        _cancelInvitationHandler = cancelInvitationHandler;
        _getMyInvitationsHandler = getMyInvitationsHandler;
        _getMyMembershipsHandler = getMyMembershipsHandler;
        _acceptInvitationHandler = acceptInvitationHandler;
        _declineInvitationHandler = declineInvitationHandler;
        _getMembersHandler = getMembersHandler;
        _fireWorkerHandler = fireWorkerHandler;
        _giftVouchersHandler = giftVouchersHandler;
        _recallVoucherHandler = recallVoucherHandler;
        _blockWorkerVoucherHandler = blockWorkerVoucherHandler;
        _unblockWorkerVoucherHandler = unblockWorkerVoucherHandler;
    }

    [HttpPost("invitations")]
    [EnableRateLimiting(CompanyInvitePolicy)]
    public async Task<IActionResult> SendInvitation([FromBody] SendInvitationRequest request, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.WorkerPhoneNumber))
        {
            return BadRequest(new { error = "workerPhoneNumber is required." });
        }

        var result = await _sendInvitationHandler.HandleAsync(
            new SendInvitationCommand(userId.Value, request.WorkerPhoneNumber, request.LegalEntityId),
            cancellationToken);

        return result.Status switch
        {
            "Success" => Ok(new { invitationId = result.InvitationId, status = "Pending" }),
            "InvalidPhone" => BadRequest(new { error = result.ErrorMessage }),
            "OwnerCompanyNotFound" => BadRequest(new { error = result.ErrorMessage }),
            "CompanyNotOwned" => NotFound(new { error = result.ErrorMessage }),
            "WorkerNotFound" => BadRequest(new { error = result.ErrorMessage }),
            "CannotInviteSelf" => BadRequest(new { error = result.ErrorMessage }),
            "WorkerAlreadyMember" => Conflict(new { error = result.ErrorMessage }),
            "AlreadyPending" => Conflict(new { error = result.ErrorMessage }),
            _ => BadRequest(new { error = result.ErrorMessage ?? "Failed to send invitation." })
        };
    }

    [HttpGet("invitations")]
    public async Task<IActionResult> GetOwnerInvitations([FromQuery] Guid? legalEntityId, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var result = await _getOwnerInvitationsHandler.HandleAsync(new GetOwnerInvitationsQuery(userId.Value, legalEntityId), cancellationToken);
        return Ok(result);
    }

    [HttpDelete("invitations/{id:guid}")]
    public async Task<IActionResult> CancelInvitation([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var result = await _cancelInvitationHandler.HandleAsync(new CancelInvitationCommand(id, userId.Value), cancellationToken);
        return result.Status switch
        {
            "Success" => Ok(new { success = true }),
            "NotFound" => NotFound(new { error = result.ErrorMessage }),
            "InvalidStatus" => Conflict(new { error = result.ErrorMessage }),
            _ => BadRequest(new { error = result.ErrorMessage ?? "Failed to cancel invitation." })
        };
    }

    [HttpGet("my-invitations")]
    public async Task<IActionResult> GetMyInvitations(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var result = await _getMyInvitationsHandler.HandleAsync(new GetMyInvitationsQuery(userId.Value), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// The companies the caller works for (epic #103 S5). Drives the "worker
    /// context" in the mobile switcher — without it a member has no context for
    /// the fuel issued to them, because <c>GET /api/legal-entity/mine</c> lists
    /// owned entities only.
    /// </summary>
    [HttpGet("my-memberships")]
    public async Task<IActionResult> GetMyMemberships(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var result = await _getMyMembershipsHandler.HandleAsync(new GetMyMembershipsQuery(userId.Value), cancellationToken);
        return Ok(result);
    }

    [HttpPost("invitations/{id:guid}/accept")]
    public async Task<IActionResult> AcceptInvitation([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var result = await _acceptInvitationHandler.HandleAsync(new AcceptInvitationCommand(id, userId.Value), cancellationToken);
        return result.Status switch
        {
            "Success" => Ok(new { success = true, memberId = result.MemberId }),
            "NotFound" => NotFound(new { error = result.ErrorMessage }),
            "InvalidStatus" => Conflict(new { error = result.ErrorMessage }),
            "AlreadyMember" => Conflict(new { error = result.ErrorMessage }),
            _ => BadRequest(new { error = result.ErrorMessage ?? "Failed to accept invitation." })
        };
    }

    [HttpPost("invitations/{id:guid}/decline")]
    public async Task<IActionResult> DeclineInvitation([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var result = await _declineInvitationHandler.HandleAsync(new DeclineInvitationCommand(id, userId.Value), cancellationToken);
        return result.Status switch
        {
            "Success" => Ok(new { success = true }),
            "NotFound" => NotFound(new { error = result.ErrorMessage }),
            "InvalidStatus" => Conflict(new { error = result.ErrorMessage }),
            _ => BadRequest(new { error = result.ErrorMessage ?? "Failed to decline invitation." })
        };
    }

    [HttpGet("members")]
    public async Task<IActionResult> GetMembers([FromQuery] Guid? legalEntityId, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var result = await _getMembersHandler.HandleAsync(new GetMembersQuery(userId.Value, legalEntityId), cancellationToken);
        return Ok(result);
    }

    [HttpDelete("members/{id:guid}")]
    public async Task<IActionResult> FireWorker([FromRoute] Guid id, [FromQuery] Guid? legalEntityId, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var result = await _fireWorkerHandler.HandleAsync(new FireWorkerCommand(userId.Value, id, legalEntityId), cancellationToken);
        return result.Status switch
        {
            "Success" => Ok(new { success = true, blockedVoucherCount = result.BlockedVoucherCount }),
            "OwnerCompanyNotFound" => BadRequest(new { error = result.ErrorMessage }),
            "CompanyNotOwned" => NotFound(new { error = result.ErrorMessage }),
            "NotFound" => NotFound(new { error = result.ErrorMessage }),
            _ => BadRequest(new { error = result.ErrorMessage ?? "Failed to fire worker." })
        };
    }

    [HttpPost("vouchers/gift")]
    public async Task<IActionResult> GiftVouchers([FromBody] GiftVouchersRequest request, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        if (request.WorkerUserId == Guid.Empty)
        {
            return BadRequest(new { error = "workerUserId is required." });
        }

        if (request.VoucherIds is null || request.VoucherIds.Count == 0)
        {
            return BadRequest(new { error = "voucherIds must contain at least one voucher id." });
        }

        var result = await _giftVouchersHandler.HandleAsync(
            new GiftVouchersCommand(userId.Value, request.WorkerUserId, request.VoucherIds, request.LegalEntityId),
            cancellationToken);

        return result.Status switch
        {
            "Success" => Ok(new { success = true, giftedCount = result.GiftedCount, issuanceOrderId = result.IssuanceOrderId }),
            "OwnerCompanyNotFound" => BadRequest(new { error = result.ErrorMessage }),
            "CompanyNotOwned" => NotFound(new { error = result.ErrorMessage }),
            "WorkerNotMember" => BadRequest(new { error = result.ErrorMessage }),
            "EmptyVoucherList" => BadRequest(new { error = result.ErrorMessage }),
            "VoucherNotFound" => NotFound(new { error = result.ErrorMessage }),
            "VoucherNotEligible" => Conflict(new { error = result.ErrorMessage }),
            _ => BadRequest(new { error = result.ErrorMessage ?? "Failed to gift vouchers." })
        };
    }

    [HttpPost("vouchers/recall/{voucherId:guid}")]
    public async Task<IActionResult> RecallVoucher([FromRoute] Guid voucherId, [FromQuery] Guid? legalEntityId, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var result = await _recallVoucherHandler.HandleAsync(new RecallVoucherCommand(userId.Value, voucherId, legalEntityId), cancellationToken);

        return result.Status switch
        {
            "Success" => Ok(new { success = true }),
            "OwnerCompanyNotFound" => BadRequest(new { error = result.ErrorMessage }),
            "CompanyNotOwned" => NotFound(new { error = result.ErrorMessage }),
            "NotFound" => NotFound(new { error = result.ErrorMessage }),
            "Forbidden" => Forbid(),
            "InvalidState" => Conflict(new { error = result.ErrorMessage }),
            _ => BadRequest(new { error = result.ErrorMessage ?? "Failed to recall voucher." })
        };
    }

    [HttpPost("vouchers/block/{voucherId:guid}")]
    public async Task<IActionResult> BlockWorkerVoucher([FromRoute] Guid voucherId, [FromQuery] Guid? legalEntityId, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var result = await _blockWorkerVoucherHandler.HandleAsync(new BlockWorkerVoucherCommand(userId.Value, voucherId, legalEntityId), cancellationToken);

        return result.Status switch
        {
            "Success" => Ok(new { success = true }),
            "OwnerCompanyNotFound" => BadRequest(new { error = result.ErrorMessage }),
            "CompanyNotOwned" => NotFound(new { error = result.ErrorMessage }),
            "NotFound" => NotFound(new { error = result.ErrorMessage }),
            "Forbidden" => Forbid(),
            "InvalidState" => Conflict(new { error = result.ErrorMessage }),
            _ => BadRequest(new { error = result.ErrorMessage ?? "Failed to block voucher." })
        };
    }

    [HttpPost("vouchers/unblock/{voucherId:guid}")]
    public async Task<IActionResult> UnblockWorkerVoucher([FromRoute] Guid voucherId, [FromQuery] Guid? legalEntityId, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var result = await _unblockWorkerVoucherHandler.HandleAsync(new UnblockWorkerVoucherCommand(userId.Value, voucherId, legalEntityId), cancellationToken);

        return result.Status switch
        {
            "Success" => Ok(new { success = true }),
            "OwnerCompanyNotFound" => BadRequest(new { error = result.ErrorMessage }),
            "CompanyNotOwned" => NotFound(new { error = result.ErrorMessage }),
            "NotFound" => NotFound(new { error = result.ErrorMessage }),
            "Forbidden" => Forbid(),
            "InvalidState" => Conflict(new { error = result.ErrorMessage }),
            _ => BadRequest(new { error = result.ErrorMessage ?? "Failed to unblock voucher." })
        };
    }

    private Guid? GetUserId()
    {
        var value = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? User.FindFirst("sub")?.Value;

        return Guid.TryParse(value, out var id) ? id : null;
    }
}

public sealed class SendInvitationRequest
{
    public string WorkerPhoneNumber { get; set; } = string.Empty;
    public Guid? LegalEntityId { get; set; }
}

public sealed class GiftVouchersRequest
{
    public Guid WorkerUserId { get; set; }
    public List<Guid> VoucherIds { get; set; } = new();
    public Guid? LegalEntityId { get; set; }
}

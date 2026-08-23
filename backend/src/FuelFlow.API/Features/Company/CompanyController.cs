using System.Security.Claims;
using FuelFlow.Features.Company.AcceptInvitation;
using FuelFlow.Features.Company.CancelInvitation;
using FuelFlow.Features.Company.DeclineInvitation;
using FuelFlow.Features.Company.FireWorker;
using FuelFlow.Features.Company.GetMembers;
using FuelFlow.Features.Company.GetMyInvitations;
using FuelFlow.Features.Company.GetOwnerInvitations;
using FuelFlow.Features.Company.GiftVouchers;
using FuelFlow.Features.Company.RecallVoucher;
using FuelFlow.Features.Company.SendInvitation;
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
    private readonly AcceptInvitationCommandHandler _acceptInvitationHandler;
    private readonly DeclineInvitationCommandHandler _declineInvitationHandler;
    private readonly GetMembersQueryHandler _getMembersHandler;
    private readonly FireWorkerCommandHandler _fireWorkerHandler;
    private readonly GiftVouchersCommandHandler _giftVouchersHandler;
    private readonly RecallVoucherCommandHandler _recallVoucherHandler;

    public CompanyController(
        SendInvitationCommandHandler sendInvitationHandler,
        GetOwnerInvitationsQueryHandler getOwnerInvitationsHandler,
        CancelInvitationCommandHandler cancelInvitationHandler,
        GetMyInvitationsQueryHandler getMyInvitationsHandler,
        AcceptInvitationCommandHandler acceptInvitationHandler,
        DeclineInvitationCommandHandler declineInvitationHandler,
        GetMembersQueryHandler getMembersHandler,
        FireWorkerCommandHandler fireWorkerHandler,
        GiftVouchersCommandHandler giftVouchersHandler,
        RecallVoucherCommandHandler recallVoucherHandler)
    {
        _sendInvitationHandler = sendInvitationHandler;
        _getOwnerInvitationsHandler = getOwnerInvitationsHandler;
        _cancelInvitationHandler = cancelInvitationHandler;
        _getMyInvitationsHandler = getMyInvitationsHandler;
        _acceptInvitationHandler = acceptInvitationHandler;
        _declineInvitationHandler = declineInvitationHandler;
        _getMembersHandler = getMembersHandler;
        _fireWorkerHandler = fireWorkerHandler;
        _giftVouchersHandler = giftVouchersHandler;
        _recallVoucherHandler = recallVoucherHandler;
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
            new SendInvitationCommand(userId.Value, request.WorkerPhoneNumber),
            cancellationToken);

        return result.Status switch
        {
            "Success" => Ok(new { invitationId = result.InvitationId, status = "Pending" }),
            "InvalidPhone" => BadRequest(new { error = result.ErrorMessage }),
            "OwnerCompanyNotFound" => BadRequest(new { error = result.ErrorMessage }),
            "WorkerNotFound" => BadRequest(new { error = result.ErrorMessage }),
            "CannotInviteSelf" => BadRequest(new { error = result.ErrorMessage }),
            "WorkerAlreadyMember" => Conflict(new { error = result.ErrorMessage }),
            "AlreadyPending" => Conflict(new { error = result.ErrorMessage }),
            _ => BadRequest(new { error = result.ErrorMessage ?? "Failed to send invitation." })
        };
    }

    [HttpGet("invitations")]
    public async Task<IActionResult> GetOwnerInvitations(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var result = await _getOwnerInvitationsHandler.HandleAsync(new GetOwnerInvitationsQuery(userId.Value), cancellationToken);
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
    public async Task<IActionResult> GetMembers(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var result = await _getMembersHandler.HandleAsync(new GetMembersQuery(userId.Value), cancellationToken);
        return Ok(result);
    }

    [HttpDelete("members/{id:guid}")]
    public async Task<IActionResult> FireWorker([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var result = await _fireWorkerHandler.HandleAsync(new FireWorkerCommand(userId.Value, id), cancellationToken);
        return result.Status switch
        {
            "Success" => Ok(new { success = true, blockedVoucherCount = result.BlockedVoucherCount }),
            "OwnerCompanyNotFound" => BadRequest(new { error = result.ErrorMessage }),
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
            new GiftVouchersCommand(userId.Value, request.WorkerUserId, request.VoucherIds),
            cancellationToken);

        return result.Status switch
        {
            "Success" => Ok(new { success = true, giftedCount = result.GiftedCount }),
            "OwnerCompanyNotFound" => BadRequest(new { error = result.ErrorMessage }),
            "WorkerNotMember" => BadRequest(new { error = result.ErrorMessage }),
            "EmptyVoucherList" => BadRequest(new { error = result.ErrorMessage }),
            "VoucherNotFound" => NotFound(new { error = result.ErrorMessage }),
            "VoucherNotEligible" => Conflict(new { error = result.ErrorMessage }),
            _ => BadRequest(new { error = result.ErrorMessage ?? "Failed to gift vouchers." })
        };
    }

    [HttpPost("vouchers/recall/{voucherId:guid}")]
    public async Task<IActionResult> RecallVoucher([FromRoute] Guid voucherId, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var result = await _recallVoucherHandler.HandleAsync(new RecallVoucherCommand(userId.Value, voucherId), cancellationToken);

        return result.Status switch
        {
            "Success" => Ok(new { success = true }),
            "OwnerCompanyNotFound" => BadRequest(new { error = result.ErrorMessage }),
            "NotFound" => NotFound(new { error = result.ErrorMessage }),
            "Forbidden" => Forbid(),
            "InvalidState" => Conflict(new { error = result.ErrorMessage }),
            _ => BadRequest(new { error = result.ErrorMessage ?? "Failed to recall voucher." })
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
}

public sealed class GiftVouchersRequest
{
    public Guid WorkerUserId { get; set; }
    public List<Guid> VoucherIds { get; set; } = new();
}

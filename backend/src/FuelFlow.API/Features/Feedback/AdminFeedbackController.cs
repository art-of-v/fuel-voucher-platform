using System.Security.Claims;
using FuelFlow.Features.Feedback.DeleteAdminFeedback;
using FuelFlow.Features.Feedback.GetAdminFeedback;
using FuelFlow.Features.Feedback.GetAdminFeedbackById;
using FuelFlow.Features.Feedback.GetAdminFeedbackFacets;
using FuelFlow.Features.Feedback.SharedModels;
using FuelFlow.Features.Feedback.UpdateFeedbackStatus;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Feedback;

/// <summary>
/// The durable record behind the Telegram staff group: the chat is where the team works, this is
/// where the backlog is searchable, triaged and measurable. Everything the group shows arrives
/// here first, and everything it dropped is still here too.
/// </summary>
[ApiController]
[Route("api/admin/feedback")]
public sealed class AdminFeedbackController : ControllerBase
{
    private readonly GetAdminFeedbackFacetsQueryHandler _getFacetsHandler;
    private readonly GetAdminFeedbackQueryHandler _getAllHandler;
    private readonly GetAdminFeedbackByIdQueryHandler _getByIdHandler;
    private readonly UpdateFeedbackStatusCommandHandler _statusHandler;
    private readonly DeleteAdminFeedbackCommandHandler _deleteHandler;

    public AdminFeedbackController(
        GetAdminFeedbackFacetsQueryHandler getFacetsHandler,
        GetAdminFeedbackQueryHandler getAllHandler,
        GetAdminFeedbackByIdQueryHandler getByIdHandler,
        UpdateFeedbackStatusCommandHandler statusHandler,
        DeleteAdminFeedbackCommandHandler deleteHandler)
    {
        _getFacetsHandler = getFacetsHandler;
        _getAllHandler = getAllHandler;
        _getByIdHandler = getByIdHandler;
        _statusHandler = statusHandler;
        _deleteHandler = deleteHandler;
    }

    /// <summary>Distinct values present in the table, so the admin's filter dropdowns list what
    /// actually exists rather than every enum member the code knows about.</summary>
    [HttpGet("facets")]
    [Authorize(Policy = AuthPolicies.Staff)]
    public async Task<IActionResult> GetFacets(CancellationToken ct = default)
    {
        var facets = await _getFacetsHandler.HandleAsync(new GetAdminFeedbackFacetsQuery(), ct);

        return Ok(facets);
    }

    /// <summary>
    /// The list. Defaults to kept messages only: the whole point of the triage step is that the
    /// team reads fewer, better rows, and a default of "everything" would put "йоук" back at the
    /// top of the page.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = AuthPolicies.Staff)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 50,
        [FromQuery] string? verdict = null,
        [FromQuery] string? category = null,
        [FromQuery] string? severity = null,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        [FromQuery] decimal? maxConfidence = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        var page = await _getAllHandler.HandleAsync(
            new GetAdminFeedbackQuery(
                Offset: offset,
                Limit: limit,
                Verdict: verdict,
                Category: category,
                Severity: severity,
                Status: status,
                Search: search,
                MaxConfidence: maxConfidence,
                From: from,
                To: to),
            ct);

        return Ok(page);
    }

    /// <summary>One message plus every answer the team gave it.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = AuthPolicies.Staff)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
    {
        var detail = await _getByIdHandler.HandleAsync(new GetAdminFeedbackByIdQuery(id), ct);

        return detail is null ? ApiError.NotFound("Not found") : Ok(detail);
    }

    /// <summary>Moves rows between review states. Re-posting an id that is already in the
    /// requested state is a no-op rather than an error, so a double-click cannot rewrite who
    /// handled a message.</summary>
    [HttpPost("status")]
    [Authorize(Policy = AuthPolicies.StaffOperations)]
    public async Task<IActionResult> UpdateStatus(
        [FromBody] UpdateFeedbackStatusRequest request,
        CancellationToken ct = default)
    {
        var result = await _statusHandler.HandleAsync(
            new UpdateFeedbackStatusCommand(request.Ids, request.Status, CurrentUserId(), CurrentUserName()),
            ct);

        return result.Status switch
        {
            "NoIds" => ApiError.BadRequest(result.Error, FeedbackRefusalCodes.NoIds),
            "UnknownStatus" => ApiError.BadRequest(result.Error, FeedbackRefusalCodes.UnknownStatus),
            "Unauthenticated" => ApiError.Unauthorized("Unauthorized"),
            _ => Ok(new FeedbackMutationResponse(true, result.Affected))
        };
    }

    /// <summary>
    /// Deletes rows outright. This is the "clear the junk" button: the triage step keeps dropped
    /// messages so a drifting classifier stays visible, and someone has to be able to act on that.
    /// </summary>
    [HttpDelete]
    [Authorize(Policy = AuthPolicies.StaffOperations)]
    public async Task<IActionResult> Delete(
        [FromQuery] Guid? id = null,
        [FromQuery] FeedbackVerdict? verdict = null,
        CancellationToken ct = default)
    {
        var result = await _deleteHandler.HandleAsync(new DeleteAdminFeedbackCommand(id, verdict), ct);

        return result.Status switch
        {
            "Refused" => ApiError.BadRequest(result.Error, FeedbackRefusalCodes.DeleteRefused),
            _ => Ok(new FeedbackMutationResponse(true, result.Affected))
        };
    }

    private Guid? CurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? User.FindFirst("sub")?.Value;

        return Guid.TryParse(claim, out var parsed) ? parsed : null;
    }

    private string? CurrentUserName() =>
        User.FindFirst(ClaimTypes.Name)?.Value
        ?? User.FindFirst("first_name")?.Value;
}

public sealed record AdminFeedbackDto
{
    public Guid Id { get; init; }
    public Guid? UserId { get; init; }
    public long TelegramChatId { get; init; }
    public string Text { get; init; } = null!;
    public FeedbackVerdict? Verdict { get; init; }
    public FeedbackCategory? Category { get; init; }
    public FeedbackSeverity? Severity { get; init; }
    public string? Summary { get; init; }
    public FeedbackLanguage? Language { get; init; }
    public decimal? Confidence { get; init; }
    public string? ClassifierError { get; init; }
    public DateTime? ClassifiedAtUtc { get; init; }
    public FeedbackStatus Status { get; init; }
    public long? StaffMessageId { get; init; }
    public DateTime? StaffRepliedAtUtc { get; init; }
    public string? AppVersion { get; init; }
    public string? Platform { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public int ReplyCount { get; set; }
}

public sealed record FeedbackReplyDto
{
    public Guid Id { get; init; }
    public string? StaffUserName { get; init; }
    public string Text { get; init; } = null!;
    public long? DeliveredMessageId { get; init; }
    public string? DeliveryError { get; init; }
    public DateTime SentAtUtc { get; init; }
}

public sealed record UpdateFeedbackStatusRequest
{
    public IReadOnlyList<Guid> Ids { get; init; } = [];
    public string Status { get; init; } = nameof(FeedbackStatus.Handled);
}

/// <summary>Named rather than anonymous: the admin client and the tests bind to these, so a rename
/// on the server breaks the build instead of silently producing nulls in both.</summary>
public sealed record FeedbackListResponse(int Total, IReadOnlyList<AdminFeedbackDto> Items);

public sealed record FeedbackDetailResponse(AdminFeedbackDto Item, IReadOnlyList<FeedbackReplyDto> Replies);

public sealed record FeedbackFacetsResponse(
    IReadOnlyList<FeedbackCategory> Categories,
    IReadOnlyList<FeedbackStatus> Statuses,
    int Total,
    int Kept,
    int Junk,
    int Unclassified,
    int Replied);

public sealed record FeedbackMutationResponse(bool Success, int Affected);

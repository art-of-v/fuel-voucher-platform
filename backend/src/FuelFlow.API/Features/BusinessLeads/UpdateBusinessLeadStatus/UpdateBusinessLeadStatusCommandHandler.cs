using System.Text.Json;
using FuelFlow.Features.Brands;
using FuelFlow.Features.BusinessLeads.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.BusinessLeads.UpdateBusinessLeadStatus;

public sealed class UpdateBusinessLeadStatusCommandHandler
{
    private const int MaxIdsPerStatusCall = 200;
    private readonly ApplicationDbContext _context;
    private readonly AuditEventService _events;

    public UpdateBusinessLeadStatusCommandHandler(
        ApplicationDbContext context,
        AuditEventService events)
    {
        _context = context;
        _events = events;
    }

    public async Task<UpdateBusinessLeadStatusResult> HandleAsync(
        UpdateBusinessLeadStatusCommand command,
        CancellationToken ct = default)
    {
        if (command.Ids is not { Count: > 0 })
            return UpdateBusinessLeadStatusResult.NoIds();

        if (!Enum.TryParse<BusinessLeadStatus>(command.Status, true, out var status))
            return UpdateBusinessLeadStatusResult.UnknownStatus(command.Status);

        if (command.ActingUserId is null)
            return UpdateBusinessLeadStatusResult.Unauthenticated();

        var actingId = command.ActingUserId.Value;
        var ids = command.Ids.Distinct().Take(MaxIdsPerStatusCall).ToList();
        var updatedAt = SystemClock.UtcNow;

        var changed = await _context.ExecuteReplaySafeAsync(async token =>
        {
            var updated = await _context.BusinessLeads
                .Where(l => ids.Contains(l.Id) && l.Status != status)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(l => l.Status, status)
                    .SetProperty(l => l.UpdatedAtUtc, updatedAt), token);

            if (updated > 0)
            {
                _events.RecordEvent(
                    "BusinessLead",
                    ids.Count == 1 ? ids[0].ToString() : $"bulk:{updated}",
                    "BusinessLeadStatusChanged",
                    null,
                    JsonSerializer.Serialize(new { count = updated, status = status.ToString() }),
                    actingId,
                    command.ActingUserName,
                    $"Moved {updated} business lead(s) to {status}",
                    actingId.ToString());

                await _context.SaveChangesAsync(token);
            }

            return updated;
        }, ct);

        return UpdateBusinessLeadStatusResult.Done(changed);
    }
}

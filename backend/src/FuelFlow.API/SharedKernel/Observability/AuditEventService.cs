using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Time;

namespace FuelFlow.Features.Brands;

/// <summary>
/// The audit trail: the rows that say who changed what, and when.
/// </summary>
/// <remarks>
/// <para>
/// <b>It does not save.</b> The row is tracked in the caller's <see cref="ApplicationDbContext"/>
/// and committed by the caller's own <c>SaveChangesAsync</c> - the same one that commits the
/// change the row describes, or the one inside the caller's transaction. That is the whole
/// point of the service: a trail that can miss a change that was made is not a trail.
/// </para>
/// <para>
/// It used to call its own <c>SaveChangesAsync</c>, and every caller saved its business change
/// first - so the two writes were two transactions, and a failure between them left a committed
/// change with no row explaining it. The other half of the same gap: a row for a change that was
/// rolled back. Planning #533 (C-3).
/// </para>
/// <para>
/// The pattern is the one <see cref="FuelFlow.Features.Pricing.PriceHistoryRecorder"/> already
/// uses for a price: the recorder stamps and appends, the caller owns the transaction boundary.
/// </para>
/// </remarks>
public sealed class AuditEventService
{
    private readonly ApplicationDbContext _context;

    public AuditEventService(ApplicationDbContext context) => _context = context;

    /// <summary>
    /// Tracks the audit row for one change. The caller commits it together with the change.
    /// </summary>
    /// <param name="aggregateType">What kind of thing changed - "Voucher", "User", "Payment".</param>
    /// <param name="aggregateId">Which one, as text.</param>
    /// <param name="eventType">What happened - "VoucherUpdated", "UserBanned".</param>
    /// <param name="oldValue">The value before, as JSON. Null when there was none.</param>
    /// <param name="newValue">The value after, as JSON. A jsonb column, so never a bare string.</param>
    /// <param name="changedByUserId">Who did it. The empty guid means a machine did.</param>
    /// <param name="changedByUserName">Their name, so the row still reads after a rename.</param>
    /// <param name="summary">One line for the audit screen.</param>
    /// <param name="providerId">The scope the row belongs to - the brand, or "all".</param>
    public void RecordEvent(
        string aggregateType,
        string aggregateId,
        string eventType,
        string? oldValue,
        string newValue,
        Guid changedByUserId,
        string? changedByUserName,
        string summary,
        string providerId)
    {
        _context.Set<AuditEventOutbox>().Add(new AuditEventOutbox
        {
            Id = Guid.CreateVersion7(),
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            EventType = eventType,
            OldValue = oldValue,
            NewValue = newValue,
            ChangedByUserId = changedByUserId == Guid.Empty ? null : changedByUserId,
            ChangedByUserName = changedByUserName,
            Summary = summary,
            ChangedAtUtc = SystemClock.UtcNow,
            ScopeId = providerId
        });
    }
}

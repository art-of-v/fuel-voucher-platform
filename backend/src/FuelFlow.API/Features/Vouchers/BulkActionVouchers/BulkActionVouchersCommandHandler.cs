using FuelFlow.API.BackgroundJobs;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Events;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FuelFlow.Features.Vouchers.BulkActionVouchers;

public sealed class BulkActionVouchersCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly AuditEventService _eventService;

    public BulkActionVouchersCommandHandler(
        ApplicationDbContext context,
        IBackgroundJobClient backgroundJobClient,
        AuditEventService eventService)
    {
        _context = context;
        _backgroundJobClient = backgroundJobClient;
        _eventService = eventService;
    }

    public async Task<BulkActionResult> HandleAsync(
        BulkActionVouchersCommand command,
        CancellationToken cancellationToken = default)
    {
if (command.Action == "delete_all")
        {
            // Soft, never hard (#217), and no ToListAsync: the old version materialised the entire
            // voucher table into memory before deleting it, which on a stock table is both a spike
            // and a pointless transfer. ExecuteUpdateAsync flips the flag in the database.
            //
            // The global !IsDeleted query filter means this only ever touches rows that are still
            // visible, so a second "delete all" cannot re-flag an already-deleted voucher.
            //
            // Filtered to stock, deliberately. This action archives EVERY voucher in the system and
            // the admin UI confirms it with the global total, so unfiltered it buries the paid ones
            // and reports success - the exact shape of bug #284. Refusing the whole action instead
            // is not an option either: the admin cannot select "everything except the paid ones",
            // so the button would be dead from the first sale onwards. Stock is archived, and what
            // was kept is named rather than left for the admin to infer from a list that did not
            // empty.
            var visibleCount = await _context.FuelVouchers.CountAsync(cancellationToken);
            var removedCount = await _context.FuelVouchers
                .Where(VoucherDeletionGuard.IsStock)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(v => v.IsDeleted, true)
                        .SetProperty(v => v.UpdatedAtUtc, DateTime.UtcNow),
                    cancellationToken);

            var keptCount = visibleCount - removedCount;

            if (command.ActingAdminUserId.HasValue)
            {
                await _eventService.RecordEventAsync(
                    "Voucher",
                    "all",
                    "VoucherBulkAction",
                    null,
                    JsonSerializer.Serialize(new { action = "delete_all", count = removedCount, kept = keptCount }),
                    command.ActingAdminUserId.Value,
                    command.ActingAdminName,
                    keptCount > 0
                        ? $"Bulk action delete_all archived {removedCount} vouchers, kept {keptCount} that were paid"
                        : $"Bulk action delete_all removed {removedCount} vouchers",
                    "all",
                    cancellationToken);
            }

            if (keptCount > 0)
            {
                // Success=false so the message reaches the admin: the controller only surfaces Error
                // on a failure, and a partial "delete all" that says nothing is indistinguishable
                // from one that missed rows.
                return new BulkActionResult
                {
                    Success = false,
                    Count = removedCount,
                    Error = $"Archived {removedCount} voucher(s), kept {keptCount}. A paid voucher is never " +
                            "deleted - the kept ones are still in the list. Delete them in batches if " +
                            "some of them are stock you meant to archive."
                };
            }

            return new BulkActionResult { Success = true, Count = removedCount };
        }

        if (command.Ids is null || command.Ids.Count == 0)
            return new BulkActionResult { Success = false, Error = "No IDs provided" };

        var entities = await _context.FuelVouchers
            .Where(v => command.Ids.Contains(v.Id))
            .ToListAsync(cancellationToken);

        var actor = TryGetActor(command);
        var preImages = entities.ToDictionary(e => e.Id, e => e.Status);
        var now = DateTime.UtcNow;

        switch (command.Action)
        {
            case "activate":
            {
                // Slice 2a gate: never activate (put on sale) stock that has no cost of its own — the
                // pricing engine would have nothing to price from. The cost lives on the voucher, so
                // the check is per voucher rather than per batch: a batch is only costed once every
                // voucher in it is, and a partly-entered batch must not put the costed part on sale.
                var uncosted = entities.Where(e => e.CostPerLiter is null).ToList();
                if (uncosted.Count > 0)
                    return new BulkActionResult
                    {
                        Success = false,
                        Error = $"Cannot activate — enter the cost (собівартість) first for {uncosted.Count} voucher(s): {string.Join(", ", uncosted.Take(10).Select(e => e.VoucherNumber))}"
                    };
                foreach (var e in entities) { e.Status = VoucherStatus.Available; e.UpdatedAtUtc = now; RecordStatusChange(e, preImages[e.Id], VoucherStatus.Available, now, actor); }
                break;
            }
            case "expire":
                foreach (var e in entities) { e.Status = VoucherStatus.Expired; e.UpdatedAtUtc = now; RecordStatusChange(e, preImages[e.Id], VoucherStatus.Expired, now, actor); }
                break;
            case "deactivate":
                foreach (var e in entities) { e.Status = VoucherStatus.Deactivated; e.UpdatedAtUtc = now; RecordStatusChange(e, preImages[e.Id], VoucherStatus.Deactivated, now, actor); }
                break;
            case "assign":
                return new BulkActionResult
                {
                    Success = false,
                    Error = "Manual assign is disabled — vouchers are assigned by fulfilling an order"
                };
            case "delete":
                // Soft, never hard (#217). A voucher is history: it may carry a cost an operator
                // typed, be the surrendered half of an exchange, or be the stock a valuation was
                // computed from. Deleting the row destroys all of that with nothing to point at,
                // and the only trace left was a bulk-event row carrying a COUNT - not which ones.
                //
                // The previous hard delete was only accidentally safe: RESTRICT foreign keys made
                // it throw for any voucher that had been fulfilled, exchanged or renewed, so it
                // destroyed exactly the vouchers with no history and refused the rest with a raw
                // FK error. The wrong set, for the wrong reason.
                //
                // Going soft removed that accidental refusal, so the rule it was standing in for
                // now has to be stated: a paid voucher is never deleted. Refusing the WHOLE batch
                // rather than skipping the paid ones, because an admin deleting 50 stock vouchers
                // must not have 3 of them silently archived - the count would still look right.
                var refused = entities.Where(VoucherDeletionGuard.IsPaidOrHeld).ToList();
                if (refused.Count > 0)
                {
                    return new BulkActionResult
                    {
                        Success = false,
                        Error = refused.Count == entities.Count
                            ? $"None of the {refused.Count} voucher(s) can be deleted. " +
                              VoucherDeletionGuard.Reason(refused[0])
                            : $"{refused.Count} of {entities.Count} voucher(s) cannot be deleted and " +
                              "none were changed. " + VoucherDeletionGuard.Reason(refused[0])
                    };
                }

                foreach (var e in entities)
                {
                    e.IsDeleted = true;
                    e.UpdatedAtUtc = now;

                    // D-13. Archive is not a status change: `is_deleted` is a separate column from
                    // `status` and a ticket can be archived while sitting in any state. So this is an
                    // `Archived` event carrying no from/to at all — which is also why ck_voucher_events_
                    // status_changed stays satisfied without knowing what the status was.
                    if (actor is { } archiveActor)
                    {
                        _context.VoucherEvents.Add(new VoucherEvent
                        {
                            Id = Guid.CreateVersion7(),
                            VoucherId = e.Id,
                            EventType = VoucherEventTypes.Archived,
                            ActorKind = VoucherActorKinds.User,
                            ActorUserId = archiveActor,
                            OccurredAtUtc = now
                        });
                    }
                }
                break;
            default:
                return new BulkActionResult { Success = false, Error = $"Unknown action: {command.Action}" };
        }

        // Every action, including delete, now mutates the entity rather than removing it, so every action
        // has to mark its entities dirty. The old guard skipped this for "delete" because
        // RemoveRange was what persisted that branch; with the flag set instead, skipping it left
        // the deletion silently unapplied - a bulk delete that reported success and deleted nothing.
        _context.FuelVouchers.UpdateRange(entities);

        await _context.SaveChangesAsync(cancellationToken);

        if (command.ActingAdminUserId.HasValue)
        {
            await _eventService.RecordEventAsync(
                "Voucher",
                "bulk",
                "VoucherBulkAction",
                null,
                JsonSerializer.Serialize(new
                {
                    action = command.Action,
                    count = entities.Count,
                    targetUserId = command.TargetUserId
                }),
                command.ActingAdminUserId.Value,
                command.ActingAdminName,
                $"Bulk action {command.Action} on {entities.Count} vouchers",
                "all",
                cancellationToken);
        }

        if (command.Action == "activate" && entities.Count > 0)
        {
            // Newly-available stock may satisfy orders stuck in PendingFulfillment (the
            // "imported != available" gap), so kick the fulfillment worker to re-drive them.
            // This direct enqueue IS the trigger — we deliberately do NOT write an
            // OutboxEventType.VoucherActivated row: nothing consumes that type, so it only
            // left permanently-unprocessed rows that inflate the admin "unprocessed outbox
            // events" alert (GetReconciliationQueryHandler). The activate action itself is
            // already audited via AuditEventService above.
            _backgroundJobClient.Enqueue<FulfillmentService>(
                s => s.ProcessPendingOrdersAsync(CancellationToken.None));
        }

        return new BulkActionResult { Success = true, Count = entities.Count };
    }

    /// <summary>
    /// D-13. Records a status transition, or deliberately nothing when there was not one.
    /// </summary>
    /// <remarks>
    /// The no-op filter is the reason this is a method rather than three lines in the branches.
    /// <c>command.Ids</c> arrives from the admin UI with <b>no status filter at all</c>, so
    /// "activate" on an already-<c>Available</c> voucher is entirely reachable — and
    /// <c>ck_voucher_events_status_changed</c> refuses <c>from_status = to_status</c>. Left
    /// unfiltered, that CHECK violation aborts the whole <c>SaveChangesAsync</c> and takes the
    /// status write down with it: an admin clicking Activate on already-active stock would get an
    /// error instead of a no-op. The pre-image is therefore captured before the switch and the
    /// event is skipped rather than attempted.
    /// </remarks>
    private void RecordStatusChange(
        FuelVoucher voucher,
        VoucherStatus from,
        VoucherStatus to,
        DateTime occurredAtUtc,
        Guid? actor)
    {
        if (from == to || actor is null)
        {
            return;
        }
        _context.VoucherEvents.Add(new VoucherEvent
        {
            Id = Guid.CreateVersion7(),
            VoucherId = voucher.Id,
            EventType = VoucherEventTypes.StatusChanged,
            FromStatus = from.ToString(),
            ToStatus = to.ToString(),
            ActorKind = VoucherActorKinds.User,
            ActorUserId = actor,
            OccurredAtUtc = occurredAtUtc
        });
    }

    /// <summary>
    /// Who is performing this bulk action, or <c>null</c> when the platform cannot say.
    /// </summary>
    /// <remarks>
    /// This is a judgement call and reviewers should see it as one.
    /// <c>AdminVoucherController.GetUserId</c> returns <c>Guid.Empty</c> rather than null when the
    /// <c>NameIdentifier</c> claim is missing. That value is NOT NULL, so it satisfies
    /// <c>ck_voucher_events_actor_named</c> — the row would name a user who does not exist, which is
    /// the exact thing the actor arrangement exists to prevent, so it is treated as no actor at all.
    /// <para>
    /// With no actor the event is skipped rather than written as <c>system</c>: an administrator
    /// pressed this button, and attributing it to the scheduler would be a different falsehood rather
    /// than a smaller one. This mirrors what <see cref="AuditEventService"/> already does with the
    /// same id.
    /// </para>
    /// </remarks>
    private static Guid? TryGetActor(BulkActionVouchersCommand command)
        => command.ActingAdminUserId is { } id && id != Guid.Empty ? id : null;
}

public sealed class BulkActionResult
{
    public bool Success { get; set; }
    public int Count { get; set; }
    public string? Error { get; set; }
}

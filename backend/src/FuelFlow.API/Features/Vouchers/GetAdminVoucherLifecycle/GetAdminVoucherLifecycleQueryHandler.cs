using FuelFlow.Features.Vouchers.Events;
using FuelFlow.Features.Vouchers.History;
using FuelFlow.Features.Vouchers.Movements;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.GetAdminVoucherLifecycle;

/// <summary>One voucher's whole recorded life.</summary>
/// <param name="Id">The voucher to read.</param>
public sealed record GetAdminVoucherLifecycleQuery(Guid Id);

/// <summary>
/// One voucher, as the operator needs to read it: what it is, where its paper has been, what has been
/// done to it, and what it cost — in one time-ordered list.
///
/// <para>
/// <b>This is the reader D-13 was built for.</b> Nineteen writers now record every handover and every
/// transition, and nothing read them: the audit existed and could not be opened. Before this, "when did
/// this voucher become sellable and who said so" had no answer for any voucher in the system.
/// </para>
/// <para>
/// <b>Three sources, one list.</b> The ledger is the spine — <c>voucher_movements</c> for custody,
/// <c>voucher_events</c> for state — and the customer's own paid renewals are woven in from
/// <see cref="VoucherHistoryProjector"/>, because they are the one thing the ledger cannot carry: a
/// renewal is money the customer handed over, and the event records the term it bought, not what was
/// paid. Sorting by time is the whole point; a list grouped by source would not be a lifecycle.
/// </para>
/// <para>
/// <b>The lineage is walked, not assumed.</b> A replace issues a different physical voucher for the same
/// tank of fuel, so "everything that happened to this fuel" spans several ids. The chain is expanded to a
/// fixpoint before anything is read, because the renewal that PRODUCED this voucher is keyed on the
/// voucher it replaced and would otherwise be invisible.
/// </para>
/// </summary>
public sealed class GetAdminVoucherLifecycleQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetAdminVoucherLifecycleQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<VoucherLifecycleResponse?> HandleAsync(
        GetAdminVoucherLifecycleQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.Id == Guid.Empty)
            return null;

        // IncludingSoftDeleted: an archived voucher is still a voucher with a history, and the one thing
        // an operator most wants to look up is the one somebody archived. The global filter is a read
        // policy for lists, not for the audit.
        var voucher = await _context.FuelVouchers
            .IncludingSoftDeleted()
            .AsNoTracking()
            .AsSplitQuery()
            .Include(v => v.Supplier)
            .Include(v => v.ImportJob)
            .FirstOrDefaultAsync(v => v.Id == query.Id, cancellationToken);

        if (voucher is null)
            return null;

        // Both ledger reads are indexed on (voucher_id, occurred_at_utc), so this is the shape the
        // indexes were built for rather than a scan.
        var events = await _context.VoucherEvents
            .AsNoTracking()
            .Where(e => e.VoucherId == query.Id)
            .OrderBy(e => e.OccurredAtUtc)
            .ToListAsync(cancellationToken);

        var movements = await _context.VoucherMovements
            .AsNoTracking()
            .Where(m => m.VoucherId == query.Id)
            .OrderBy(m => m.OccurredAtUtc)
            .ToListAsync(cancellationToken);

        var money = await LoadLineageAsync(query.Id, voucher.Liters, cancellationToken);

        var entries = new List<VoucherLifecycleEntry>();

        foreach (var e in events)
        {
            entries.Add(new VoucherLifecycleEntry
            {
                Source = VoucherLifecycleSources.Event,
                Type = e.EventType,
                OccurredAtUtc = e.OccurredAtUtc,
                From = e.FromStatus,
                To = e.ToStatus,
                PreviousCustomerExpiration = e.PreviousCustomerExpiration,
                NewCustomerExpiration = e.NewCustomerExpiration,
                ActorKind = e.ActorKind,
                ActorUserId = e.ActorUserId,
                Reason = e.Reason,
                ExternalRef = e.ExternalRef
            });
        }

        foreach (var m in movements)
        {
            entries.Add(new VoucherLifecycleEntry
            {
                Source = VoucherLifecycleSources.Movement,
                Type = m.Kind,
                OccurredAtUtc = m.OccurredAtUtc,
                From = m.FromLocation,
                To = m.ToLocation,
                ActorKind = m.ActorKind,
                ActorUserId = m.ActorUserId,
                AmountUah = m.AmountUah,
                CostDeltaUah = m.CostDeltaUah,
                Reason = m.Reason,
                OrderId = m.OrderId
            });
        }

        foreach (var p in money.Events)
        {
            entries.Add(new VoucherLifecycleEntry
            {
                Source = VoucherLifecycleSources.Money,
                Type = p.Type.ToString(),
                OccurredAtUtc = p.Date,
                // The projector's own vocabulary: an extend keeps the voucher's id, a replace issues a
                // different one. Passing the distinction through is what lets the page say "replaced"
                // rather than mislabelling a swap as an extension.
                From = p.IsReplacement ? "replaced" : "extended",
                To = null,
                AmountUah = p.Amount,
                PreviousCustomerExpiration = p.ValidFrom,
                NewCustomerExpiration = p.ValidTo,
                TermCode = p.TermCode
            });
        }

        // One list, in time order. Ties are broken by source so the order is stable between calls:
        // a movement and an event written in the same statement must not swap places per request.
        var ordered = entries
            .OrderBy(e => e.OccurredAtUtc)
            .ThenBy(e => e.Source, StringComparer.Ordinal)
            .ThenBy(e => e.Type, StringComparer.Ordinal)
            .ToList();

        // actor_user_id is FK-free by design — the audit row must outlive its author. Resolving it for
        // display is therefore allowed but may come back empty, and a row with no name is NOT rewritten
        // into "unknown": the id is still there, and it is still answerable.
        var actorIds = ordered
            .Where(e => e.ActorUserId is not null)
            .Select(e => e.ActorUserId!.Value)
            .Distinct()
            .ToList();

        var actorNames = actorIds.Count == 0
            ? []
            : await _context.Users
                .AsNoTracking()
                .Where(u => actorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, ActorNameOf, cancellationToken);

        foreach (var entry in ordered)
        {
            if (entry.ActorUserId is { } id && actorNames.TryGetValue(id, out var name))
            {
                entry.ActorName = name;
            }
        }

        return new VoucherLifecycleResponse
        {
            VoucherId = voucher.Id,
            VoucherNumber = voucher.VoucherNumber,
            Status = voucher.Status.ToString(),
            Liters = voucher.Liters,
            Brand = voucher.Brand,
            IsArchived = voucher.IsDeleted,

            SupplierId = voucher.SupplierId,
            SupplierName = voucher.Supplier?.Name,
            CostPerLiter = voucher.CostPerLiter,
            SupplierExpirationDate = voucher.SupplierExpirationDate,
            CustomerExpirationDate = voucher.CustomerExpirationDate,

            ImportJobId = voucher.ImportJobId,
            ImportFileName = voucher.ImportJob?.FileName,
            ImportedAtUtc = voucher.ImportJob?.CompletedAtUtc,

            RootVoucherId = money.RootVoucherId,
            Lineage = money.Lineage,

            Entries = ordered
        };
    }

    /// <summary>First name, then whatever is there. A user with no first name is still somebody.</summary>
    private static string ActorNameOf(User user)
        => string.IsNullOrWhiteSpace(user.FirstName)
            ? user.PhoneNumber
            : (string.IsNullOrWhiteSpace(user.LastName) ? user.FirstName : $"{user.FirstName} {user.LastName}");

    /// <summary>
    /// The paid renewals in this voucher's lineage, plus the root and the chain it came through.
    /// </summary>
    /// <remarks>
    /// <b>Why the chain is expanded first, in BOTH directions.</b> A replace issues a different voucher
    /// for the same fuel, and the renewal is keyed on the voucher it <i>replaced</i> — so the one that
    /// produced the requested voucher lives under an id we have not met yet, and it is the one carrying
    /// the money. Walking only forwards finds nothing: following renewals <i>from</i> this voucher goes
    /// to whatever came after it, and the replace that produced it points the other way. So the set is
    /// grown to a fixpoint, forwards and backwards together.
    /// <para>
    /// Both walks are keyed on tables indexed the same way, and the loop terminates because
    /// <c>lineage</c> only ever grows — which is also the cycle guard on a malformed chain.
    /// </para>
    /// </remarks>
    private async Task<LineageProjection> LoadLineageAsync(
        Guid voucherId,
        decimal nominalLiters,
        CancellationToken cancellationToken)
    {
        var lineage = new HashSet<Guid> { voucherId };
        var grew = true;

        while (grew)
        {
            grew = false;
            var lineageIds = lineage.ToList();

            // ---- Forwards: renewals OF anything we know, and whatever they delivered. ----
            var lines = await _context.OrderLineItems
                .AsNoTracking()
                .Where(l => l.SourceVoucherId != null && lineageIds.Contains(l.SourceVoucherId.Value))
                .ToListAsync(cancellationToken);

            if (lines.Count != 0)
            {
                var lineIds = lines.Select(l => l.Id).ToList();

                // IncludingReleasedFulfilments: the global filter hides RELEASED deliveries, and a renewal whose
                // voucher was later handed back is still part of this fuel's history — it moved no
                // paper, but the customer paid and the term moved.
                var delivered = await _context.Fulfillments
                    .IncludingReleasedFulfilments()
                    .AsNoTracking()
                    .Where(f => f.LineItemId != null && lineIds.Contains(f.LineItemId.Value))
                    .ToListAsync(cancellationToken);

                foreach (var f in delivered)
                {
                    if (f.VoucherId != Guid.Empty && lineage.Add(f.VoucherId))
                        grew = true;
                }
            }

            // ---- Backwards: whatever DELIVERED anything we know, and the voucher it replaced. ----
            var producingLineIds = await _context.Fulfillments
                .IncludingReleasedFulfilments()
                .AsNoTracking()
                .Where(f => lineageIds.Contains(f.VoucherId) && f.LineItemId != null)
                .Select(f => f.LineItemId!.Value)
                .ToListAsync(cancellationToken);

            if (producingLineIds.Count != 0)
            {
                var sources = await _context.OrderLineItems
                    .AsNoTracking()
                    .Where(l => l.SourceVoucherId != null && producingLineIds.Contains(l.Id))
                    .Select(l => l.SourceVoucherId!.Value)
                    .ToListAsync(cancellationToken);

                foreach (var source in sources)
                {
                    if (lineage.Add(source))
                        grew = true;
                }
            }
        }

        var all = lineage.ToList();

        // Every renewal line acting on anything in the chain, which now includes the ones upstream of
        // the requested voucher as well as downstream.
        var renewalLines = await _context.OrderLineItems
            .AsNoTracking()
            .Where(l => l.SourceVoucherId != null && all.Contains(l.SourceVoucherId.Value))
            .ToListAsync(cancellationToken);

        if (renewalLines.Count == 0)
        {
            return new LineageProjection(voucherId, [voucherId], []);
        }

        var renewalLineIds = renewalLines.Select(l => l.Id).ToList();

        var deliveredByLine = new Dictionary<Guid, Fulfillment>();
        foreach (var f in await _context.Fulfillments
                     .IncludingReleasedFulfilments()
                     .AsNoTracking()
                     .Where(f => f.LineItemId != null && renewalLineIds.Contains(f.LineItemId.Value))
                     .OrderBy(f => f.FulfilledAtUtc)
                     .ToListAsync(cancellationToken))
        {
            // A renewal line delivers exactly one voucher. Ascending means a line that somehow carries
            // two keeps the later delivery - defensive, not expected.
            deliveredByLine[f.LineItemId!.Value] = f;
        }

        var ordersById = await _context.Orders
            .AsNoTracking()
            .Where(o => renewalLines.Select(l => l.OrderId).Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, cancellationToken);

        var nodes = renewalLines
            .Select(l =>
            {
                deliveredByLine.TryGetValue(l.Id, out var f);
                return new VoucherHistoryProjector.RenewalNode(
                    l.SourceVoucherId!.Value,
                    f?.VoucherId,
                    f?.FulfilledAtUtc ?? ordersById.GetValueOrDefault(l.OrderId)?.CreatedAtUtc ?? default,
                    l.TermCode,
                    l.LineTotal,
                    l.PreviousCustomerExpiration,
                    l.NewCustomerExpiration);
            })
            .ToList();

        var built = VoucherHistoryProjector.Build(voucherId, nominalLiters, nodes, _ => null);

        var root = VoucherHistoryProjector.ResolveRootVoucherId(voucherId, nodes);

        return new LineageProjection(root, BuildChain(voucherId, nodes), built);
    }

    /// <summary>
    /// Oldest first. Replaces walk BACKWARD (a renewal that produced this voucher names its source), so
    /// the walk is the mirror of the projector's emit order.
    /// </summary>
    private static List<Guid> BuildChain(Guid currentVoucherId, IReadOnlyCollection<VoucherHistoryProjector.RenewalNode> renewals)
    {
        var chain = new List<Guid> { currentVoucherId };
        var cursor = currentVoucherId;
        var seen = new HashSet<Guid> { cursor };

        while (true)
        {
            var producedByReplace = renewals.FirstOrDefault(r =>
                r.FulfilledVoucherId == cursor && r.SourceVoucherId != cursor);
            if (producedByReplace.FulfilledVoucherId is null) break;
            if (!seen.Add(producedByReplace.SourceVoucherId)) break;   // cycle guard
            cursor = producedByReplace.SourceVoucherId;
            chain.Insert(0, cursor);
        }

        return chain;
    }

    private sealed record LineageProjection(
        Guid RootVoucherId,
        List<Guid> Lineage,
        List<VoucherHistoryEventDto> Events);
}

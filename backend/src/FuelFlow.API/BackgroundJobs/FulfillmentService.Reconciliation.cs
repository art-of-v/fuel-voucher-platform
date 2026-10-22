using FuelFlow.API.BackgroundJobs.Models;
using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Events;
using FuelFlow.Features.Vouchers.Movements;
using FuelFlow.Features.Vouchers.Renewal;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.Payments.SharedModels;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FuelFlow.API.BackgroundJobs;

public partial class FulfillmentService
{

    private async Task FixMismatchedFulfillmentsAsync(CancellationToken cancellationToken)
    {
        const int batchSize = 50;
        int skip = 0;
        bool hasMore;

        do
        {
            var fulfilledOrders = await _context.Orders
                .Include(o => o.LineItems)
                // Renewal orders are fulfilled by ProcessRenewalOrderAsync (two-branch: extend the
                // source voucher in place, or replace it from stock). They have no
                // one-stock-voucher-per-unit shape, so this liter-count trimmer must never touch
                // them — it would strip a renewal fulfillment and wrongly flip an extended/source
                // voucher back to Available. Exclude them.
                .Where(o => o.Status == OrderStatus.Fulfilled
                            && o.Kind != OrderKind.Renewal
                            // Company issuances are one voucher per unit by construction, and their
                            // line items carry the company's internal cost rather than a sale price.
                            // Same reasoning as renewals above: not this trimmer's business.
                            && o.Kind != OrderKind.ReceivedFromCompany)
                .OrderBy(o => o.Id)
                .Skip(skip)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            hasMore = fulfilledOrders.Count == batchSize;
            skip += batchSize;

            foreach (var order in fulfilledOrders)
            {
                var currentOrder = order;

                // #246: this runs on the same scoped context as AssignVouchersToOrderAsync, which
                // claims vouchers with raw compare-and-swap UPDATEs. Those rows are then loaded here
                // TRACKED and saved below — and the tracker still holds the instances the assignment
                // path loaded before its statements bumped them. With a concurrency token on
                // fuel_vouchers that is not a race, it is a guaranteed conflict on a legitimate save.
                //
                // So the tracker is emptied before the read, exactly as the assignment path does on
                // entry: everything loaded after this point carries the values the database has now,
                // and the only conflicts left are real ones. Without the Clear() an AsNoTracking()
                // here would be worse, not better — the rows read fresh would collide with the stale
                // instances already tracked, and `Update()` would throw on the duplicate key.
                _context.ChangeTracker.Clear();

                // The counts the alerts announce: the body fills them, the alerts read them after
                // the commit.
                Dictionary<decimal, int> lineItemCounts = new();
                Dictionary<decimal, int> voucherCounts = new();

                // How many vouchers this order is about to have taken back, held until the
                // transaction has committed. 0 means there is nothing to announce afterwards.
                var notifyMismatch = 0;

                // Set by the body when it leaves an old mismatch alone for a human. The alert that
                // says so goes out after the commit, like the release alert below.
                var mismatchLeftForAHuman = false;

                async Task ApplyAsync()
                {
                    if (_context.Database.IsRelational())
                    {
                        // Serialize trimming per order so it can never race with a concurrent
                        // fulfillment that is assigning vouchers to the same order.
                        await _context.Database.ExecuteSqlInterpolatedAsync(
                            $"SELECT pg_advisory_xact_lock(hashtext('fulfillment-order'), hashtext({currentOrder.Id.ToString()}))",
                            cancellationToken);

                        // Loaded TRACKED, and that is load-bearing in a way that is easy to undo by accident.
                        //
                        // `orders` carries the xmin concurrency token (#246), and EF materializes a
                        // store-generated token ONLY on a tracked load — on an AsNoTracking read the
                        // property comes back as 0. This order is reset and saved further down, so with
                        // AsNoTracking the UPDATE carried `WHERE id = @id AND xmin = 0`, matched nothing,
                        // and threw a concurrency exception on EVERY mismatch repair. Not a race: a
                        // guarantee of failure. The tracker was emptied above, so nothing else is
                        // holding this key and the tracked read is safe.
                        var freshOrder = await _context.Orders
                            .Include(o => o.LineItems)
                            .FirstOrDefaultAsync(o => o.Id == currentOrder.Id, cancellationToken);

                        if (freshOrder == null || freshOrder.Status != OrderStatus.Fulfilled)
                        {
                            return;
                        }

                        currentOrder = freshOrder;
                    }

                    lineItemCounts = currentOrder.LineItems?
                        .GroupBy(li => li.Liters)
                        .ToDictionary(g => g.Key, g => g.Sum(li => li.Quantity))
                        ?? [];

                    var fulfillments = await _context.Fulfillments
                        .Where(f => f.OrderId == currentOrder.Id)
                        .ToListAsync(cancellationToken);

                    var fulfillmentVoucherIds = fulfillments.Select(f => f.VoucherId).ToList();

                    var vouchers = fulfillmentVoucherIds.Count != 0
                        ? await _context.FuelVouchers
                            .Where(v => fulfillmentVoucherIds.Contains(v.Id))
                            .ToListAsync(cancellationToken)
                        : [];

                    var vouchersById = vouchers.ToDictionary(v => v.Id);

                    voucherCounts = vouchers
                        .GroupBy(v => v.Liters)
                        .ToDictionary(g => g.Key, g => g.Count());

                    // Per-line comparison is only sound when every fulfilment names its line. A single unattributed
                    // row would make the per-line counts disagree with reality in a way that reads as a short delivery,
                    // and the repair below would then take fuel from a customer to "fix" it.
                    var fullyAttributed = fulfillments.Count > 0 && fulfillments.All(f => f.LineItemId.HasValue);

                    var hasMismatch = fullyAttributed
                        ? HasPerLineMismatch(currentOrder.LineItems, fulfillments)
                        : lineItemCounts.Any(kv =>
                            !voucherCounts.TryGetValue(kv.Key, out var count) || count != kv.Value);

                    if (!fullyAttributed && fulfillments.Count > 0)
                    {
                        // Worth saying, because this is the weaker comparison and an operator reading the log should know
                        // which one produced the verdict below.
                        _logger.LogInformation(
                            "Order {OrderId}: {Unattributed} of {Total} fulfilments carry no line, so the count is compared by litres",
                            currentOrder.Id, fulfillments.Count(f => f.LineItemId is null), fulfillments.Count);
                    }

                    // #217: a mismatch is a SIGNAL, not a verdict. Acting on it destroys a real
                    // delivery record and takes the fuel back from whoever holds it - and the only
                    // evidence is the same row being deleted. A young order is the case where the
                    // signal is trustworthy (a double webhook, an order just seeded wrong); an old
                    // one is where a wrong count would strip fuel from a real customer, so it is
                    // left alone for a human. Same shape as the grace window #960 gave the loss job.
                    if (hasMismatch && !IsWithinReconciliationGrace(currentOrder))
                    {
                        _logger.LogWarning(
                            "Fulfillment count for order {OrderId} does not match its line items, but the order is older than {GraceMinutes} minutes - left untouched. Line items want {Wanted}, found {Found}.",
                            currentOrder.Id, ReconciliationGraceMinutes, Describe(lineItemCounts), Describe(voucherCounts));

                        // #217 step 3: this is the case that most needs a human - nothing was
                        // corrected, so the mismatch just stays until someone opens the order. A log
                        // line nobody reads is not a warning.
                        //
                        // The transaction is closed BEFORE the alert. The alert is a Telegram HTTP
                        // call, and holding a transaction open across one is the thing the comment
                        // further down this method warns about. Nothing was written on this path,
                        // so it is closed without a write — which releases the advisory lock and
                        // the row locks with it. The alert itself waits for the commit: the flag
                        // below carries the decision out of the transaction.
                        mismatchLeftForAHuman = true;

                        return;
                    }

                    if (hasMismatch)
                    {
                        _logger.LogInformation(
                            "Fixing mismatched fulfillments for order {OrderId}", currentOrder.Id);

                        var excessVouchers = new List<(Fulfillment Fulfillment, FuelVoucher Voucher)>();
                        var releasedCount = 0;

                        // Release the excess out of the line that was over-delivered, not out of a
                        // litres bucket. Under the litres fallback the two are the same thing; under a
                        // per-line verdict they are not, and releasing across lines would take fuel
                        // from a customer whose own line was satisfied correctly.
                        var excessToRelease = fullyAttributed
                            ? ExcessPerLine(currentOrder.LineItems, fulfillments)
                            : ExcessPerLitres(lineItemCounts, voucherCounts, fulfillments, vouchers);

                        foreach (var fulfillment in excessToRelease)
                        {
                            if (vouchersById.TryGetValue(fulfillment.VoucherId, out var voucher))
                            {
                                excessVouchers.Add((fulfillment, voucher));
                            }
                        }

                        foreach (var (fulfillment, voucher) in excessVouchers)
                        {
                            // #217 step 2: mark the fulfillment released, do not erase it. The row
                            // is the record that a delivery happened; the correction is the fact that
                            // it was undone, and both are true at once. Deleting it destroyed the
                            // only evidence, in the same statement that took the fuel away.
                            var releasedAtUtc = SystemClock.UtcNow;
                            fulfillment.ReleasedAtUtc = releasedAtUtc;
                            fulfillment.ReleaseReason = "quantity_mismatch";

                            // Read before the flip: the candidate set is not status-filtered here, so
                            // the voucher may be Assigned, Used or anything else a fulfilment row can
                            // still point at, and the history has to say which one it was rather than
                            // asserting a single one.
                            var fromStatus = voucher.Status;

                            voucher.Status = VoucherStatus.Available;
                            voucher.AssignedToUserId = null;
                            voucher.LegalEntityId = null;
                            voucher.WorkerUserId = null;
                            // Back in the warehouse means back under no order. A released fulfillment
                            // still points at one, so the index on order_id is filtered to live rows -
                            // otherwise a stock voucher that still claims an order would show up as
                            // delivered in that order's history.
                            voucher.OrderId = null;
                            voucher.UpdatedAtUtc = releasedAtUtc;
                            _context.Fulfillments.Update(fulfillment);
                            _context.FuelVouchers.Update(voucher);

                            // D-13, and the reason `order_id = null` above is the tell: this is CUSTODY,
                            // not just state. The paper goes back from a customer into the warehouse, so
                            // it owes a `returned` movement as well as the status change. One row for
                            // either alone would lose half of what happened.
                            //
                            // `system`, not `user`: a nightly reconciliation found the mismatch, nobody
                            // pressed anything. The customer is the voucher_id on the movement, not its
                            // author — attributing a scheduler's correction to them would be a different
                            // falsehood.
                            if (fromStatus != VoucherStatus.Available)
                            {
                                _context.VoucherEvents.Add(new VoucherEvent
                                {
                                    Id = Guid.CreateVersion7(),
                                    VoucherId = voucher.Id,
                                    EventType = VoucherEventTypes.StatusChanged,
                                    FromStatus = fromStatus.ToString(),
                                    ToStatus = nameof(VoucherStatus.Available),
                                    // The order is the reason this happened, and it is the causal link a
                                    // reader needs: "which order gave back fuel, and when".
                                    ExternalRef = currentOrder.Id.ToString(),
                                    ActorKind = VoucherActorKinds.System,
                                    ActorUserId = null,
                                    OccurredAtUtc = releasedAtUtc
                                });
                            }

                            _context.VoucherMovements.Add(new VoucherMovement
                            {
                                Id = Guid.CreateVersion7(),
                                Kind = VoucherMovementKinds.Returned,
                                VoucherId = voucher.Id,
                                FromLocation = VoucherLocations.Customer,
                                ToLocation = VoucherLocations.Pool,
                                OrderId = currentOrder.Id,
                                ActorKind = VoucherActorKinds.System,
                                ActorUserId = null,
                                // The customer-side order line is what the litres were compared against.
                                LineItemId = fulfillment.LineItemId,
                                OccurredAtUtc = releasedAtUtc
                            });

                            _logger.LogInformation(
                                "Released fulfillment {FulfillmentId} for voucher {VoucherId} ({Liters}L) from order {OrderId} - the record is kept, the voucher is back in stock",
                                fulfillment.Id, voucher.Id, voucher.Liters, currentOrder.Id);

                            releasedCount++;
                        }

                        var remainingCount = fulfillments.Count - excessVouchers.Count;

                        currentOrder.Status = remainingCount > 0
                            ? OrderStatus.PartiallyFulfilled
                            : OrderStatus.PendingFulfillment;
                        currentOrder.FulfilledAtUtc = null;
                        currentOrder.PartiallyFulfilledSinceUtc = currentOrder.Status == OrderStatus.PartiallyFulfilled
                            ? SystemClock.UtcNow
                            : null;
                        currentOrder.UpdatedAtUtc = SystemClock.UtcNow;
                        // No Update() call. The entity is tracked and its properties are mutated in
                        // place, so change detection handles it - and Update() would mark the whole
                        // graph Modified, rewriting every line item of the order to change four fields
                        // on the order itself.
                        await _context.SaveChangesAsync(cancellationToken);

                        _logger.LogInformation(
                            "Order {OrderId} reset to {Status} after removing {Count} mismatched fulfillments",
                            currentOrder.Id, currentOrder.Status, excessVouchers.Count);

                        // The alert is deferred to after the commit, and only its arguments are
                        // captured here. The first version sent it from inside the transaction,
                        // right here, while a comment above it claimed the opposite - so a commit
                        // that failed one line later had already told the operator vouchers were
                        // back on the shelf. Holding a transaction open across a Telegram call is a
                        // second reason not to.
                        notifyMismatch = releasedCount;
                    }
                }

                // The transaction is DISPOSED at the end of this iteration, so every exit path closes
                // it: a commit where the work was done, a rollback everywhere else.
                //
                // It used to be opened and closed by hand, and one path — the one that leaves an old
                // mismatch alone for a human — jumped over the commit. There was no `finally` either,
                // and the `catch` only runs on an exception, so that path left the transaction open on
                // the connection. The next order's BeginTransactionAsync then threw "The connection is
                // already in a transaction and cannot participate in another transaction", which
                // killed the whole batch rather than the one order — and the abandoned transaction
                // held its row locks until the connection closed, which blocked the writers behind it.
                //
                // The helper opens the transaction, commits it at the end of the body and rolls it
                // back when the body throws, so every exit path closes it again.
                //
                // The transaction runs through ReplaySafeTransaction, so the execution strategy may
                // replay it: a transient failure retries the body rather than losing the repair.
                await _context.ExecuteReplaySafeAsync(_ => ApplyAsync(), cancellationToken);

                // The mismatch that was left alone for a human. The alert goes out now, with the
                // transaction closed: it is a Telegram HTTP call, and the body left it waiting
                // rather than sending it from inside the transaction.
                if (mismatchLeftForAHuman)
                {
                    await _notifications.FulfillmentMismatchAsync(
                        currentOrder.Id.ToString(),
                        currentOrder.Id.ToString()[..8],
                        currentOrder.LineItems?.FirstOrDefault()?.Brand ?? "?",
                        0,
                        lineItemCounts.Values.Sum(),
                        voucherCounts.Values.Sum(),
                        corrected: false,
                        cancellationToken);

                    continue;
                }

                // The release is durable now, so it is safe to announce it. If this starts
                // repeating for one order, the reconciliation is reading a wrong quantity
                // rather than finding a real mismatch; the per-order throttle key keeps that
                // from becoming a loop of alerts.
                if (notifyMismatch > 0)
                {
                    await _notifications.FulfillmentMismatchAsync(
                        currentOrder.Id.ToString(),
                        currentOrder.Id.ToString()[..8],
                        currentOrder.LineItems?.FirstOrDefault()?.Brand ?? "?",
                        notifyMismatch,
                        lineItemCounts.Sum(l => l.Value),
                        voucherCounts.Sum(v => v.Value),
                        corrected: true,
                        cancellationToken);
                }

                if (currentOrder.Status == OrderStatus.PartiallyFulfilled)
                {
                    await TryAutoRefundAsync(currentOrder.Id, cancellationToken);
                }
            }
        } while (hasMore);
    }

    /// Within the automatic-correction window. Clock comes from the order's own fulfilment time rather
    /// than from "now", so a replayed or re-run job behaves the same as the original.
    private static bool IsWithinReconciliationGrace(Order order)
    {
        var since = SystemClock.UtcNow - (order.FulfilledAtUtc ?? order.CreatedAtUtc);
        return since <= TimeSpan.FromMinutes(ReconciliationGraceMinutes);
    }

    /// <summary>
    /// Whether the order was delivered what each of its lines asked for.
    /// </summary>
    /// <remarks>
    /// The litres-only comparison this replaces could not tell two lines of the same volume apart.
    /// An order for "OKKO A95 10 L x2" and "WOG A95 10 L x1" that received three OKKO vouchers and
    /// no WOG voucher produced <c>10 L: 3</c> on both sides and passed. The customer was short a line,
    /// the order read as fulfilled, and the repair - which released the excess from the same litres
    /// bucket - had no way to know which delivery was wrong.
    /// <para>
    /// A line with nothing against it is a mismatch, which is the half of the check that mattered
    /// most: the old form could only ever see a count going the wrong way in aggregate.
    /// </para>
    /// </remarks>
    private static bool HasPerLineMismatch(
        ICollection<OrderLineItem>? lineItems,
        List<Fulfillment> fulfillments)
    {
        if (lineItems is null)
        {
            return fulfillments.Count > 0;
        }

        var delivered = fulfillments
            .GroupBy(f => f.LineItemId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var line in lineItems)
        {
            if (delivered.GetValueOrDefault(line.Id) != line.Quantity)
            {
                return true;
            }
        }

        // A fulfillment pointing at a line this order does not have would otherwise pass every
        // per-line check while adding to no bucket.
        return delivered.Keys.Any(id => lineItems.All(li => li.Id != id));
    }

    /// <summary>
    /// The fulfilments to release, newest first within whatever bucket they belong to.
    /// </summary>
    /// <remarks>
    /// Newest first because a later delivery is the more likely double: a replayed callback, a second
    /// worker that got there first. That is the same reason the litres version ordered by
    /// <c>FulfilledAtUtc</c> descending, applied per line rather than per litre count.
    /// </remarks>
    private static List<Fulfillment> ExcessPerLine(
        ICollection<OrderLineItem>? lineItems,
        List<Fulfillment> fulfillments)
    {
        var excess = new List<Fulfillment>();

        if (lineItems is null)
        {
            return excess;
        }

        foreach (var line in lineItems)
        {
            var forLine = fulfillments.Where(f => f.LineItemId == line.Id).ToList();
            var over = forLine.Count - line.Quantity;

            if (over > 0)
            {
                excess.AddRange(forLine.OrderByDescending(f => f.FulfilledAtUtc).Take(over));
            }
        }

        return excess;
    }

    /// <summary>
    /// The pre-attribute comparison, kept for fulfilments that carry no line.
    /// </summary>
    /// <remarks>
    /// Historical rows, and anything written before the column existed. It cannot tell two lines of the
    /// same volume apart, which is why it is only reached when no row names its line - and why the
    /// backfill filled in everything it could rather than leaving all of it null.
    /// </remarks>
    private static List<Fulfillment> ExcessPerLitres(
        Dictionary<decimal, int> lineItemCounts,
        Dictionary<decimal, int> voucherCounts,
        List<Fulfillment> fulfillments,
        List<FuelVoucher> vouchers)
    {
        var excess = new List<Fulfillment>();

        foreach (var (litres, count) in voucherCounts)
        {
            var over = count - lineItemCounts.GetValueOrDefault(litres, 0);

            if (over <= 0)
            {
                continue;
            }

            excess.AddRange(fulfillments
                .Join(vouchers.Where(v => v.Liters == litres), f => f.VoucherId, v => v.Id, (f, _) => f)
                .OrderByDescending(f => f.FulfilledAtUtc)
                .Take(over));
        }

        return excess;
    }

    private static string Describe(Dictionary<decimal, int> counts)
        => counts.Count == 0 ? "none" : string.Join(", ", counts.Select(kv => $"{kv.Key}L x{kv.Value}"));
}

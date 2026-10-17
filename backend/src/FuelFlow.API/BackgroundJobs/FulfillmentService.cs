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
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FuelFlow.API.BackgroundJobs;

public class FulfillmentService
{
    /// <summary>
    /// How long after fulfilment a count mismatch may still be corrected automatically (#217).
    /// <para>
    /// The reconciliation infers "this delivery never happened" from a quantity mismatch alone. That
    /// inference is safe for a young order - a double webhook or a just-created bad order - and
    /// dangerous for an old one, where a wrong count strips fuel from a customer who genuinely holds
    /// it, and deletes the only record that they did. Fifteen minutes is long enough for the webhook
    /// and outbox paths to settle and short enough that a real bad order is still cleaned up.
    /// </para>
    /// </summary>
    private const int ReconciliationGraceMinutes = 15;

    private readonly ApplicationDbContext _context;
    private readonly ILogger<FulfillmentService> _logger;
    private readonly RefundOrderCommandHandler _refundHandler;
    private readonly RuntimeSettingsService _settings;
    private readonly NotificationDispatcher _notifications;
    private readonly IConfiguration _configuration;

    public FulfillmentService(
        ApplicationDbContext context,
        ILogger<FulfillmentService> logger,
        RefundOrderCommandHandler refundHandler,
        RuntimeSettingsService settings,
        NotificationDispatcher notifications,
        IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _refundHandler = refundHandler;
        _settings = settings;
        _notifications = notifications;
        _configuration = configuration;
    }

    public async Task ProcessPendingOrdersAsync(CancellationToken cancellationToken = default)
    {
        var unprocessedEvents = await _context.OutboxEvents
            .Where(e => !e.Processed && e.EventType == OutboxEventType.OrderCreated)
            .OrderBy(e => e.CreatedAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (unprocessedEvents.Any())
        {
            _logger.LogInformation("Processing {Count} pending ORDER_CREATED events", unprocessedEvents.Count);

            foreach (var outboxEvent in unprocessedEvents)
            {
                try
                {
                    await ProcessOrderCreatedEventAsync(outboxEvent, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process outbox event {EventId}", outboxEvent.Id);
                }
            }
        }
        else
        {
            _logger.LogDebug("No pending ORDER_CREATED events to process");
        }

        await FixMismatchedFulfillmentsAsync(cancellationToken);
        await ProcessOpenOrdersBackfillAsync(cancellationToken);
        await ProcessOpenRenewalOrdersBackfillAsync(cancellationToken);
    }

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
                IDbContextTransaction? transaction = null;

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

                try
                {
                    if (_context.Database.IsRelational())
                    {
                        // Serialize trimming per order so it can never race with a concurrent
                        // fulfillment that is assigning vouchers to the same order.
                        transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

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
                            await transaction.CommitAsync(cancellationToken);
                            continue;
                        }

                        currentOrder = freshOrder;
                    }

var lineItemCounts = currentOrder.LineItems?
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

var voucherCounts = vouchers
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

                    // How many vouchers this order is about to have taken back, held until the
                    // transaction has committed. 0 means there is nothing to announce afterwards.
                    var notifyMismatch = 0;

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
                            fulfillment.ReleasedAtUtc = DateTime.UtcNow;
                            fulfillment.ReleaseReason = "quantity_mismatch";

                            voucher.Status = VoucherStatus.Available;
                            voucher.AssignedToUserId = null;
                            voucher.LegalEntityId = null;
                            voucher.WorkerUserId = null;
                            // Back in the warehouse means back under no order. A released fulfillment
                            // still points at one, so the index on order_id is filtered to live rows -
                            // otherwise a stock voucher that still claims an order would show up as
                            // delivered in that order's history.
                            voucher.OrderId = null;
                            voucher.UpdatedAtUtc = DateTime.UtcNow;
                            _context.Fulfillments.Update(fulfillment);
                            _context.FuelVouchers.Update(voucher);

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
                            ? DateTime.UtcNow
                            : null;
                        currentOrder.UpdatedAtUtc = DateTime.UtcNow;
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

                    if (transaction != null)
                    {
                        await transaction.CommitAsync(cancellationToken);
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
                catch
                {
                    if (transaction != null)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                    }
                    throw;
                }
            }
        } while (hasMore);
    }

    /// Within the automatic-correction window. Clock comes from the order's own fulfilment time rather
    /// than from "now", so a replayed or re-run job behaves the same as the original.
    private static bool IsWithinReconciliationGrace(Order order)
    {
        var since = DateTime.UtcNow - (order.FulfilledAtUtc ?? order.CreatedAtUtc);
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

    private async Task ProcessOpenOrdersBackfillAsync(CancellationToken cancellationToken)
    {
        var openOrders = await _context.Orders
            .Include(o => o.LineItems)
            // Renewal orders are backfilled by ProcessOpenRenewalOrdersBackfillAsync, not here.
            // Exclude them so the buy-fuel path never assigns stock vouchers against a renewal
            // order's line items.
            .Where(o => (o.Status == OrderStatus.PendingFulfillment || o.Status == OrderStatus.PartiallyFulfilled)
                        && o.Kind != OrderKind.Renewal)
            .OrderBy(o => o.CreatedAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (!openOrders.Any())
        {
            return;
        }

        _logger.LogInformation("Backfilling {Count} open orders", openOrders.Count);

        foreach (var order in openOrders)
        {
            try
            {
                await AssignVouchersToOrderAsync(order, null, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to backfill order {OrderId}", order.Id);
            }
        }
    }

    public async Task ProcessVoucherImportsAsync(CancellationToken cancellationToken = default)
    {
        var unprocessedEvents = await _context.OutboxEvents
            .Where(e => !e.Processed && e.EventType == OutboxEventType.VoucherExpired)
            .OrderBy(e => e.CreatedAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (!unprocessedEvents.Any())
        {
            return;
        }

        _logger.LogInformation("Processing {Count} VOUCHER_EXPIRED events", unprocessedEvents.Count);

        foreach (var outboxEvent in unprocessedEvents)
        {
            try
            {
                outboxEvent.Processed = true;
                outboxEvent.ProcessedAtUtc = DateTime.UtcNow;
                _context.OutboxEvents.Update(outboxEvent);
                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("Processed VOUCHER_EXPIRED event {EventId}", outboxEvent.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process VOUCHER_EXPIRED event {EventId}", outboxEvent.Id);
            }
        }
    }

    private async Task ProcessOrderCreatedEventAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken)
    {
        var payload = System.Text.Json.JsonSerializer.Deserialize<OrderCreatedPayload>(outboxEvent.Payload);

        if (payload?.OrderId == null)
        {
            _logger.LogWarning("Invalid ORDER_CREATED payload for event {EventId}", outboxEvent.Id);
            outboxEvent.Processed = true;
            outboxEvent.ProcessedAtUtc = DateTime.UtcNow;
            _context.OutboxEvents.Update(outboxEvent);
            await _context.SaveChangesAsync(cancellationToken);
            return;
        }

        var order = await _context.Orders
            .Include(o => o.LineItems)
            .FirstOrDefaultAsync(o => o.Id == payload.OrderId, cancellationToken);

        if (order == null)
        {
            _logger.LogWarning("Order {OrderId} not found for event {EventId}", payload.OrderId, outboxEvent.Id);
            outboxEvent.Processed = true;
            outboxEvent.ProcessedAtUtc = DateTime.UtcNow;
            _context.OutboxEvents.Update(outboxEvent);
            await _context.SaveChangesAsync(cancellationToken);
            return;
        }

        if (order.Status == OrderStatus.Fulfilled || order.Status == OrderStatus.Cancelled)
        {
            _logger.LogInformation("Order {OrderId} already {Status}, marking event as processed", order.Id, order.Status);
            outboxEvent.Processed = true;
            outboxEvent.ProcessedAtUtc = DateTime.UtcNow;
            _context.OutboxEvents.Update(outboxEvent);
            await _context.SaveChangesAsync(cancellationToken);
            return;
        }

        await AssignVouchersToOrderAsync(order, outboxEvent, cancellationToken);
    }

    private async Task AssignVouchersToOrderAsync(
        Order order,
        OutboxEvent? outboxEvent,
        CancellationToken cancellationToken)
    {
        // Each order in a batch is processed in isolation. Any entity force-attached while
        // handling a previous order (e.g. an Order via Update() in the partial path, or a
        // refund graph in the auto-refund handler) must never leak into the next order's
        // change tracker, otherwise a later SaveChanges can throw "Unexpected entry.EntityState:
        // Detached" (or an identity-conflict exception inside the refund handler).
        _context.ChangeTracker.Clear();

        var shouldAutoRefund = false;
        IDbContextTransaction? transaction = null;

        try
        {
            if (_context.Database.IsRelational())
            {
                // Serialize fulfillment per order across all processes (API replicas + JobsWorker)
                // and keep the voucher claim + fulfillment insert in ONE transaction so a failed
                // insert can never leave a voucher assigned without a fulfillment row.
                transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtext('fulfillment-order'), hashtext({order.Id.ToString()}))",
                    cancellationToken);

                var freshOrder = await _context.Orders
                    .AsNoTracking()
                    .Include(o => o.LineItems)
                    .FirstOrDefaultAsync(o => o.Id == order.Id, cancellationToken);

                if (freshOrder == null ||
                    freshOrder.Status == OrderStatus.Fulfilled ||
                    freshOrder.Status == OrderStatus.Cancelled)
                {
                    if (outboxEvent != null)
                    {
                        outboxEvent.Processed = true;
                        outboxEvent.ProcessedAtUtc = DateTime.UtcNow;
                        _context.OutboxEvents.Update(outboxEvent);
                        await _context.SaveChangesAsync(cancellationToken);
                    }

                    await transaction.CommitAsync(cancellationToken);
                    return;
                }

                order = freshOrder;
            }

            var lineItems = order.LineItems?.ToList() ?? [];
            var totalNeeded = lineItems.Sum(li => li.Quantity);

            if (totalNeeded == 0)
            {
                _logger.LogWarning("Order {OrderId} has no line items, skipping fulfillment", order.Id);
                if (outboxEvent != null)
                {
                    outboxEvent.Processed = true;
                    outboxEvent.ProcessedAtUtc = DateTime.UtcNow;
                    _context.OutboxEvents.Update(outboxEvent);
                    await _context.SaveChangesAsync(cancellationToken);
                }

                if (transaction != null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
                return;
            }

            var alreadyAssignedCount = await _context.Fulfillments
                .CountAsync(f => f.OrderId == order.Id, cancellationToken);

            var vouchersNeeded = Math.Max(0, totalNeeded - alreadyAssignedCount);

            if (vouchersNeeded == 0)
            {
                var updatedCount = await TryMarkOrderFulfilledAsync(order.Id, cancellationToken);

                if (updatedCount > 0)
                {
                    _logger.LogInformation("Order {OrderId} already had all vouchers assigned, marked as fulfilled", order.Id);
                }
                else
                {
                    _logger.LogDebug("Order {OrderId} already fulfilled by another instance", order.Id);
                }

                if (outboxEvent != null)
                {
                    outboxEvent.Processed = true;
                    outboxEvent.ProcessedAtUtc = DateTime.UtcNow;
                    _context.OutboxEvents.Update(outboxEvent);
                    await _context.SaveChangesAsync(cancellationToken);
                }

                if (transaction != null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
                return;
            }

            var vouchersAssigned = 0;
            var existingFulfillments = await _context.Fulfillments
                .Where(f => f.OrderId == order.Id)
                .ToListAsync(cancellationToken);

            var usedVoucherIds = existingFulfillments.Select(f => f.VoucherId).ToList();

            // Read-only: this feeds a count, and the rows are raw-UPDATEd by the claim below.
            // AsNoTracking so an instance loaded before a claim never carries a stale xmin into a
            // later SaveChanges (#246).
            var existingFulfillmentVouchers = usedVoucherIds.Count != 0
                ? await _context.FuelVouchers
                    .AsNoTracking()
                    .Where(v => usedVoucherIds.Contains(v.Id))
                    .ToListAsync(cancellationToken)
                : [];

            var assignedCounts = existingFulfillmentVouchers
                .GroupBy(v => new { Brand = v.Brand.ToLowerInvariant(), v.FuelTypeId, v.Liters })
                .ToDictionary(g => g.Key, g => g.Count());

            // Which stock pool this order draws from: a QA test account is served only test vouchers,
            // every real account only real stock. Resolved once per order and re-asserted at the atomic
            // claim. IgnoreQueryFilters so this matches the raw-SQL claim predicate, which sees the row
            // regardless of any soft-delete filter.
            var isTestAccount = await _context.Users
                .IgnoreQueryFilters()
                .Where(u => u.Id == order.UserId)
                .Select(u => u.IsQaAccount)
                .FirstOrDefaultAsync(cancellationToken);

            foreach (var lineItem in lineItems)
            {
                var key = new { Brand = lineItem.Brand.ToLowerInvariant(), lineItem.FuelTypeId, lineItem.Liters };
                var alreadyForThisLine = assignedCounts.GetValueOrDefault(key, 0);
                var remainingNeeded = Math.Max(0, lineItem.Quantity - alreadyForThisLine);

                for (int i = 0; i < remainingNeeded; i++)
                {
                    var availableVoucher = await FindMatchingVoucherAsync(
                        order, lineItem, usedVoucherIds, isTestAccount, cancellationToken);

                    if (availableVoucher == null)
                    {
                        _logger.LogWarning(
                            "No available voucher for order {OrderId} line item {FuelType} {Liters}L ({Assigned}/{Needed})",
                            order.Id, lineItem.FuelTypeId, lineItem.Liters, i, lineItem.Quantity);

                        // The alert goes to staff Telegram, where a raw FuelTypeId GUID is useless.
                        // Resolve the display name (e.g. "ДП ЄВРО") and include brand + litres so
                        // staff know exactly which stock to top up; fall back to the id if unmatched.
                        var fuelTypeName = await _context.FuelTypes
                            .AsNoTracking()
                            .Where(ft => ft.Id == lineItem.FuelTypeId)
                            .Select(ft => ft.Name)
                            .FirstOrDefaultAsync(cancellationToken);

                        var fuelLabel = string.IsNullOrEmpty(fuelTypeName)
                            ? lineItem.FuelTypeId
                            : $"{fuelTypeName} ({lineItem.Brand.ToUpperInvariant()}, {lineItem.Liters:0.##} L)";

                        await _notifications.OrderUnfulfillableAsync(
                            order.Id, fuelLabel, i, lineItem.Quantity, cancellationToken);
                        break;
                    }

                    var assignedCount = await TryAssignVoucherAsync(availableVoucher.Id, order.UserId, order.LegalEntityId, order.Id, CustomerExpirationFor(lineItem), cancellationToken);

                    if (assignedCount == 0)
                    {
                        _logger.LogDebug("Voucher {VoucherId} already assigned by another instance, skipping", availableVoucher.Id);
                        continue;
                    }

                    var fulfillment = new Fulfillment
                    {
                        OrderId = order.Id,
                        VoucherId = availableVoucher.Id,
                        // The line is in scope here, so this is exact rather than inferred. Without it
                        // the reconciliation can only compare litres, which cannot tell two lines of
                        // the same volume apart - see Fulfillment.LineItemId.
                        LineItemId = lineItem.Id,
                        FulfilledAtUtc = DateTime.UtcNow
                    };

                    _context.Fulfillments.Add(fulfillment);
                    usedVoucherIds.Add(availableVoucher.Id);
                    vouchersAssigned++;

                    _logger.LogInformation(
                        "Assigned voucher {VoucherId} to order {OrderId} line item {FuelType} {Liters}L ({Assigned}/{Needed})",
                        availableVoucher.Id, order.Id, lineItem.FuelTypeId, lineItem.Liters, vouchersAssigned, vouchersNeeded);
                }
            }

            await _context.SaveChangesAsync(cancellationToken);

            var totalAssigned = alreadyAssignedCount + vouchersAssigned;

            if (totalAssigned >= totalNeeded)
            {
                var updatedCount = await TryMarkOrderFulfilledAsync(order.Id, cancellationToken);

                if (updatedCount > 0)
                {
                    _logger.LogInformation("Order {OrderId} fully fulfilled", order.Id);

                    // Match the orderId field with jsonb containment, NOT a substring LIKE.
                    // payload is a jsonb column and Postgres has no `jsonb ~~ jsonb` (LIKE)
                    // operator, so String.Contains here threw 42883 and - because this sits
                    // inside the per-order fulfillment transaction - rolled back the voucher
                    // claims and the Fulfilled status with it. `@>` also only matches the
                    // orderId field, where a substring could match the id inside any other.
                    var orderProbe = System.Text.Json.JsonSerializer.Serialize(new { orderId = order.Id });

                    var hasFulfilledEvent = await _context.OutboxEvents
                        .AnyAsync(e => e.EventType == OutboxEventType.OrderFulfilled
                                    && EF.Functions.JsonContains(e.Payload, orderProbe),
                                  cancellationToken);

                    if (!hasFulfilledEvent)
                    {
                        var fulfilledEvent = new OutboxEvent
                        {
                            EventType = OutboxEventType.OrderFulfilled,
                            Payload = System.Text.Json.JsonSerializer.Serialize(new
                            {
                                orderId = order.Id,
                                userId = order.UserId,
                                fulfilledAt = DateTime.UtcNow
                            }),
                            Processed = false,
                            CreatedAtUtc = DateTime.UtcNow
                        };

                        _context.OutboxEvents.Add(fulfilledEvent);
                        await _context.SaveChangesAsync(cancellationToken);
                    }
                }
                else
                {
                    _logger.LogDebug("Order {OrderId} already marked as fulfilled by another instance", order.Id);
                }
            }
            else if (totalAssigned > 0)
            {
                var orderToUpdate = await _context.Orders
                    .FirstOrDefaultAsync(o => o.Id == order.Id &&
                               (o.Status == OrderStatus.PendingFulfillment || o.Status == OrderStatus.PartiallyFulfilled),
                               cancellationToken);

                if (orderToUpdate != null)
                {
                    orderToUpdate.Status = OrderStatus.PartiallyFulfilled;
                    orderToUpdate.FulfilledAtUtc = null;
                    // Keep the original timestamp when the order first became partially
                    // fulfilled so the auto-refund grace period is measured from then, not
                    // from every re-assignment run.
                    orderToUpdate.PartiallyFulfilledSinceUtc ??= DateTime.UtcNow;
                    orderToUpdate.UpdatedAtUtc = DateTime.UtcNow;
                    _context.Orders.Update(orderToUpdate);
                    await _context.SaveChangesAsync(cancellationToken);

                    _logger.LogInformation(
                        "Order {OrderId} partially fulfilled: {Assigned}/{Needed} vouchers assigned",
                        order.Id, totalAssigned, totalNeeded);

                    shouldAutoRefund = true;
                }
            }

            if (outboxEvent != null)
            {
                outboxEvent.Processed = true;
                outboxEvent.ProcessedAtUtc = DateTime.UtcNow;
                _context.OutboxEvents.Update(outboxEvent);
                await _context.SaveChangesAsync(cancellationToken);
            }

            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            throw;
        }

        if (shouldAutoRefund)
        {
            await TryAutoRefundAsync(order.Id, cancellationToken);
        }
    }

    private async Task<FuelVoucher?> FindMatchingVoucherAsync(
        Order order,
        OrderLineItem lineItem,
        List<Guid> usedVoucherIds,
        bool isTestAccount,
        CancellationToken cancellationToken)
    {
        // Expiry is enforced, not optional. With this filter commented out, the ascending
        // brand-term sort handed every paying customer the *most* expired stock first, and
        // nothing in the system ever flips stale rows to Expired (that is a manual admin
        // action), so the oldest unredeemable voucher was permanently at the head of the queue.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // TODO: temporary testing escape hatch — remove once the import workflow no longer
        // needs to run against expired test stock. Only ever flip it in a disposable/staging
        // environment; turning it off in production hands the most-expired stock to customers.
        var expiryEnabled = _configuration.GetValue<bool>("VoucherExpiration:Enabled", true);
        var pool = DataKinds.ForPool(isTestAccount);

        // A QA account draws only from test stock and a real account only from real stock, so a seeded
        // test voucher never reaches a paying customer and a QA run never burns real inventory.
        // Re-asserted atomically at the claim (TryAssignVoucherAsync).
        // AsNoTracking (#246): this is a decision, not a write. The chosen row is claimed by a raw
        // compare-and-swap UPDATE moments later, and tracking it here would put an instance with the
        // pre-claim xmin into the change tracker — where any later save of that row would fail on a
        // conflict that never happened.
        return await _context.FuelVouchers
            .AsNoTracking()
            .Where(v => v.Status == VoucherStatus.Available
                     && v.Brand.ToLower() == lineItem.Brand.ToLower()
                     && v.FuelTypeId == lineItem.FuelTypeId
                     && v.Liters == lineItem.Liters
                     && v.DataKind == pool
                     && (!expiryEnabled || v.SupplierExpirationDate >= today)
                     && !usedVoucherIds.Contains(v.Id))
            .OrderBy(v => v.SupplierExpirationDate)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// The customer-facing expiry for a line bought on a term: today plus that term. Null when the line
    /// carries no term, which means "sell the voucher's full remaining life" — the behaviour for every
    /// order placed before short terms existed.
    /// </summary>
    /// <remarks>
    /// A floor on the promise, not a gate on stock. A line asking for more life than the cheapest matching
    /// voucher has is still fulfilled; <see cref="TryAssignVoucherAsync"/> clamps the written date to the
    /// voucher's own supplier term, so the customer gets exactly what the supplier backs and never more.
    /// Refusing the sale here instead would strand a paid order over a stock-mix detail.
    /// </remarks>
    private static DateOnly? CustomerExpirationFor(OrderLineItem lineItem)
        => VoucherRenewalTerms.TryFromCode(lineItem.TermCode, out var term)
            ? term.ApplyTo(DateOnly.FromDateTime(DateTime.UtcNow))
            : null;

    protected internal virtual async Task<int> TryMarkOrderFulfilledAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var rowsAffected = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "orders" SET status = 'Fulfilled', fulfilled_at_utc = {DateTime.UtcNow}, updated_at_utc = {DateTime.UtcNow}, partially_fulfilled_since_utc = NULL WHERE id = {orderId} AND (status = 'PendingFulfillment' OR status = 'PartiallyFulfilled')""",
            cancellationToken);

        return rowsAffected;
    }

    protected internal virtual async Task<int> TryAssignVoucherAsync(Guid voucherId, Guid userId, Guid? legalEntityId, Guid orderId, DateOnly? customerExpiration, CancellationToken cancellationToken)
    {
        // The expiry predicate is repeated here on purpose: this UPDATE is the atomic claim, and
        // between the SELECT that chose this voucher and this statement the date can roll over
        // or an admin can edit the row. The claim itself must refuse expired stock.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var expiryEnabled = _configuration.GetValue<bool>("VoucherExpiration:Enabled", true);
        var expiryCutoff = expiryEnabled ? today : DateOnly.MinValue;

        // order_id lands in this same statement on purpose: this is the atomic claim, so the
        // voucher must never be in someone's hands while its owning order is still unknown, not
        // even for the few statements before the Fulfillment row is inserted below.
        //
        // The shortened customer expiry rides on the same statement, deliberately. Un-assignment
        // (FixMismatchedFulfillmentsAsync) puts the voucher back to Available but does not restore a
        // date, so a shortened date written by a separate statement would survive the rollback and the
        // stock would silently re-enter the pool with less life than the supplier granted.
        //
        // The supplier term is the ceiling, so the promise is clamped rather than trusted: a line whose
        // term outruns the voucher gets the voucher's full remaining life, which is what it would have
        // got before short terms existed.
        //
        // The QA/test pools stay separate at the atomic claim too: a QA account can only claim test
        // stock and a real account only real stock. A correlated subquery on the owning user keeps the
        // check race-proof, exactly like the expiry predicate above, so a flag edit between the SELECT
        // and this statement can never hand stock across the pools.
        var rowsAffected = customerExpiration is null
            ? await _context.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "fuel_vouchers" SET status = 'Assigned', assigned_to_user_id = {userId}, legal_entity_id = {legalEntityId}, worker_user_id = NULL, order_id = {orderId}, updated_at_utc = {DateTime.UtcNow} WHERE id = {voucherId} AND status = 'Available' AND supplier_expiration_date >= {expiryCutoff} AND data_kind = (SELECT CASE WHEN u.is_qa_account THEN 'qa' ELSE 'real' END FROM "users" u WHERE u.id = {userId})""",
                cancellationToken)
            : await _context.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "fuel_vouchers" SET status = 'Assigned', assigned_to_user_id = {userId}, legal_entity_id = {legalEntityId}, worker_user_id = NULL, order_id = {orderId}, customer_expiration_date = LEAST({customerExpiration.Value}, supplier_expiration_date), updated_at_utc = {DateTime.UtcNow} WHERE id = {voucherId} AND status = 'Available' AND supplier_expiration_date >= {expiryCutoff} AND data_kind = (SELECT CASE WHEN u.is_qa_account THEN 'qa' ELSE 'real' END FROM "users" u WHERE u.id = {userId})""",
                cancellationToken);

        return rowsAffected;
    }

    private async Task TryAutoRefundAsync(Guid orderId, CancellationToken cancellationToken)
    {
        try
        {
            // Auto-refund is a runtime setting, off by default. When enabled it only fires
            // after the order has been partially fulfilled for the configured grace period.
            if (!await _settings.IsAutoRefundEnabledAsync(cancellationToken))
            {
                return;
            }

            var sinceUtc = await _context.Orders
                .AsNoTracking()
                .Where(o => o.Id == orderId)
                .Select(o => o.PartiallyFulfilledSinceUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (sinceUtc is null)
            {
                return;
            }

            var delayDays = await _settings.GetAutoRefundDelayDaysAsync(cancellationToken);
            if (DateTime.UtcNow - sinceUtc.Value < TimeSpan.FromDays(delayDays))
            {
                return;
            }

            // Don't hammer Monobank when a cancel keeps failing: retry a failed auto-refund
            // at most once an hour. (Manual refunds always retry immediately on demand.)
var lastFailedAtUtc = await _context.Payments
    .AsNoTracking()
    .Where(p => p.OrderId == orderId && p.Kind == PaymentKind.Refund && p.Status == PaymentStatus.Failed)
    .Select(p => (DateTime?)p.UpdatedAtUtc)
    .OrderByDescending(r => r)
                .FirstOrDefaultAsync(cancellationToken);

            if (lastFailedAtUtc is not null &&
                DateTime.UtcNow - lastFailedAtUtc.Value < TimeSpan.FromMinutes(60))
            {
                return;
            }

            var result = await _refundHandler.HandleAsync(new RefundOrderCommand
            {
                OrderId = orderId,
                IsAutomatic = true
            }, cancellationToken);

            if (result.Status == "Processing")
            {
                _logger.LogInformation(
                    "Auto-refund {RefundId} initiated for partially fulfilled order {OrderId}: {Amount} kopecks",
                    result.RefundId, orderId, result.AmountKopecks);
            }
            else if (result.Status != "NothingToRefund" && result.Status != "NotPayable" && result.Status != "NotFound")
            {
                _logger.LogWarning(
                    "Auto-refund for order {OrderId} returned status {Status}: {Error}",
                    orderId, result.Status, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto-refund failed for order {OrderId}", orderId);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Voucher renewal / replacement fulfilment (issue #80, slice 3).
    //
    // A renewal order's lines name the voucher they renew instead of drawing one stock unit each
    // the way a buy-fuel order does, so it is fulfilled by its own two-branch path here — never by
    // AssignVouchersToOrderAsync or the mismatch trimmer (both exclude renewal orders). Everything runs only AFTER payment:
    //   • Extend  — the source voucher is still valid: push its expiry out by the bought term
    //               (old expiry + term, leftover days kept), same row/code stays with the customer.
    //   • Replace — the source voucher has lapsed: claim a fresh stock voucher of the same
    //               brand/fuel/nominal valid at least until today+term, hand it to the customer,
    //               and flip the old one to Expired.
    // Idempotency: each item's FulfilledVoucherId marker is the done-flag, so a re-run (webhook +
    // per-minute backfill, or a retry) skips lines already applied. Concurrency mirrors buy-fuel:
    // one advisory xact lock per order id, one transaction around the whole order.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Fulfils one paid renewal order: applies the extend/replace branch to each of its
    /// still-unfulfilled renewal order lines, then marks the order Fulfilled (all
    /// applied), PartiallyFulfilled (some applied — auto-refund considers the rest) or leaves it
    /// PendingFulfillment (none applied — the backfill retries and a no-stock alert was raised).
    /// </summary>
    public async Task ProcessRenewalOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        _context.ChangeTracker.Clear();

        var shouldAutoRefund = false;
        IDbContextTransaction? transaction = null;

        try
        {
            if (_context.Database.IsRelational())
            {
                transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtext('fulfillment-order'), hashtext({orderId.ToString()}))",
                    cancellationToken);
            }

            var order = await _context.Orders
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

            // Only an open, paid renewal order is actionable. Terminal or unpaid states no-op.
            if (order == null ||
                (order.Status != OrderStatus.PendingFulfillment && order.Status != OrderStatus.PartiallyFulfilled))
            {
                if (transaction != null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
                return;
            }

            // Which stock pool this renewal draws from: a QA test account is served only test
            // vouchers, every real account only real stock. Resolved once and re-asserted at the atomic
            // claim. IgnoreQueryFilters so it matches the raw-SQL claim predicate.
            var isTestAccount = await _context.Users
                .IgnoreQueryFilters()
                .Where(u => u.Id == order.UserId)
                .Select(u => u.IsQaAccount)
                .FirstOrDefaultAsync(cancellationToken);

            // The work list IS the order's renewal lines. #1180 put the intent there and the contract
            // step dissolved voucher_renewal_items, so a renewal has one home from checkout to expiry
            // rather than two that have to agree. A line is a renewal exactly when it names the voucher
            // it renews, which is what this predicate is - no Kind column to keep in step with it.
            var renewalLines = await _context.OrderLineItems
                .AsTracking()
                .Where(l => l.OrderId == orderId && l.SourceVoucherId != null)
                .ToListAsync(cancellationToken);

            if (renewalLines.Count == 0)
            {
                _logger.LogWarning("Renewal order {OrderId} has no renewal lines, skipping", orderId);
                if (transaction != null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
                return;
            }

            // The done-marker, which used to be `VoucherRenewalItem.FulfilledVoucherId IS NOT NULL` and
            // is now simply "this line already has a fulfilment". A line that failed for want of stock
            // has none, which is exactly right - it is still owed.
            //
            // IgnoreQueryFilters because the filter hides released fulfilments, and a released one is
            // still an applied one: the term was pushed either way. Treating it as unapplied would make
            // the retry sweep extend the same voucher a second time.
            var deliveredLineIds = (await _context.Fulfillments
                    .IgnoreQueryFilters()
                    .Where(f => f.OrderId == orderId && f.LineItemId != null)
                    .Select(f => f.LineItemId!.Value)
                    .ToListAsync(cancellationToken))
                .ToHashSet();

            var sourceIds = renewalLines.Select(l => l.SourceVoucherId!.Value).Distinct().ToList();
            var sources = await _context.FuelVouchers
                .AsNoTracking()
                .Where(v => sourceIds.Contains(v.Id))
                .ToListAsync(cancellationToken);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var now = DateTime.UtcNow;
            var usedStockIds = new List<Guid>();
            var fulfilledCount = 0;

            foreach (var line in renewalLines)
            {
                var sourceVoucherId = line.SourceVoucherId!.Value;

                // Done-marker: this line was already applied by an earlier run.
                if (deliveredLineIds.Contains(line.Id))
                {
                    fulfilledCount++;
                    continue;
                }

                var source = sources.FirstOrDefault(v => v.Id == sourceVoucherId);
                if (source == null)
                {
                    _logger.LogWarning(
                        "Renewal order {OrderId}: source voucher {VoucherId} not found, cannot fulfil line",
                        orderId, sourceVoucherId);
                    continue;
                }

                if (!VoucherRenewalTerms.TryFromCode(line.TermCode, out var term))
                {
                    _logger.LogWarning(
                        "Renewal order {OrderId}: unknown term code '{TermCode}' for voucher {VoucherId}",
                        orderId, line.TermCode, sourceVoucherId);
                    continue;
                }

                // Branch on what can actually be done to the source RIGHT NOW, using the same rule the
                // checkout used to price and sell this exact line. The date alone is not enough: a source
                // whose customer term still equals its supplier term has nothing to extend into, and
                // checkout already sold that as a Replace. Deciding on the date alone here would try to
                // extend, refuse past the ceiling, and leave the line unfulfilled with the customer
                // already paid - so the ceiling decides the branch, exactly as it did at sale time.
                var canExtendInPlace = source.CustomerExpirationDate >= today
                                       && VoucherRenewalEligibility.CanExtend(
                                           source.CustomerExpirationDate, source.SupplierExpirationDate, term);

                if (canExtendInPlace)
                {
                    var newExpiration = VoucherRenewalEligibility.NewExpirationForExtend(source.CustomerExpirationDate, term);

                    // Backstop, not the decision: the branch above already proved the term fits under the
                    // supplier term, so reaching here means the source row changed under us mid-transaction.
                    // Writing a customer date past the real term would promise validity the station may
                    // never honour, so we refuse rather than over-extend.
                    if (newExpiration > source.SupplierExpirationDate)
                    {
                        _logger.LogWarning(
                            "Renewal order {OrderId}: refusing to extend voucher {VoucherId} past its supplier term " +
                            "(customer {CustomerExpiration} + {TermCode} = {Requested}, brand {ProviderExpiration})",
                            orderId, source.Id, source.CustomerExpirationDate, line.TermCode, newExpiration,
                            source.SupplierExpirationDate);
                        continue;
                    }

                    var extended = await TryExtendVoucherAsync(
                        source.Id,
                        order.UserId,
                        source.CustomerExpirationDate,
                        source.SupplierExpirationDate,
                        newExpiration,
                        cancellationToken);

                    // The gate. Everything below records something that happened, so it must not run
                    // when the compare-and-swap changed nothing: a renewal that lost the race to another
                    // worker or a retry must leave no trace saying the customer was given a term they
                    // were not given.
                    if (extended == 0)
                    {
                        _logger.LogWarning(
                            "Renewal order {OrderId}: could not extend voucher {VoucherId} (not Assigned to this user, already applied, or the row changed under us)",
                            orderId, source.Id);
                        continue;
                    }

                    // The expiry snapshot is written here and nowhere else now that
                    // voucher_renewal_items is gone. It has to be here: the value it records as
                    // "previous" is one TryExtendVoucherAsync has already overwritten in place.
                    line.PreviousCustomerExpiration = source.CustomerExpirationDate;
                    line.NewCustomerExpiration = newExpiration;

                    // D-13. The same fact as an EVENT rather than a movement, because that is what it
                    // is: this pushed time forward and the paper did not go anywhere. It is the reason
                    // voucher_movements has no extend kind, and the reason relaxing
                    // ck_voucher_movements_distinct_locations would be the wrong fix.
                    //
                    // `source` was read before the CAS, so previous is the genuine before and new the
                    // genuine after. The term is not repeated: order_line_items.term_code owns it, and
                    // the order that paid is reachable through ExternalRef.
                    _context.VoucherEvents.Add(new VoucherEvent
                    {
                        Id = Guid.CreateVersion7(),
                        VoucherId = source.Id,
                        EventType = VoucherEventTypes.Extended,
                        PreviousCustomerExpiration = source.CustomerExpirationDate,
                        NewCustomerExpiration = newExpiration,
                        // The order that paid for it. The term is reachable through it -
                        // order_line_items.term_code - and repeating it here would be a second copy of a
                        // fact that already has an owner.
                        ExternalRef = orderId.ToString(),
                    ActorKind = VoucherActorKinds.User,
                        ActorUserId = order.UserId,
                        OccurredAtUtc = now
                    });

                    // The fee is revenue, not a cost adjustment: cost_per_liter records what we PAID a
                    // supplier for this voucher and nothing else moves it. See VoucherCosting.
                    _context.Fulfillments.Add(new Fulfillment
                    {
                        OrderId = orderId,
                        VoucherId = source.Id,
                        LineItemId = line.Id,
                        FulfilledAtUtc = now
                    });
                    fulfilledCount++;

                    _logger.LogInformation(
                        "Renewal order {OrderId}: extended voucher {VoucherId} to {NewExpiration} (term {TermCode})",
                        orderId, source.Id, newExpiration, line.TermCode);
                }
                else
                {
                    // The promise is what the customer paid for ON TOP of what they already hold, not
                    // today + term — so the stock floor (and the date we write on the replacement)
                    // both hang off this one value. See VoucherRenewalEligibility for why.
                    var promisedExpiration = VoucherRenewalEligibility.PromisedExpirationForReplace(
                        today, source.CustomerExpirationDate, term);
                    var stock = await FindReplacementVoucherAsync(source, promisedExpiration, usedStockIds, isTestAccount, cancellationToken);

                    if (stock == null)
                    {
                        var fuelTypeName = await _context.FuelTypes
                            .AsNoTracking()
                            .Where(ft => ft.Id == source.FuelTypeId)
                            .Select(ft => ft.Name)
                            .FirstOrDefaultAsync(cancellationToken);

                        var fuelLabel = string.IsNullOrEmpty(fuelTypeName)
                            ? source.FuelTypeId
                            : $"{fuelTypeName} ({source.Brand.ToUpperInvariant()}, {source.Liters:0.##} L)";

                        _logger.LogWarning(
                            "Renewal order {OrderId}: no replacement stock for {FuelLabel} valid until {PromisedExpiration}",
                            orderId, fuelLabel, promisedExpiration);

                        await _notifications.OrderUnfulfillableAsync(orderId, fuelLabel, 0, 1, cancellationToken);
                        continue;
                    }

                    // Inherit the SOURCE voucher's legal entity (null for a personal voucher, the
                    // company's id for a member's voucher) so a replaced company voucher stays with
                    // that company rather than silently becoming personal.
                    var claimed = await TryAssignReplacementVoucherAsync(
                        stock.Id, order.UserId, source.LegalEntityId, promisedExpiration, orderId, cancellationToken);

                    if (claimed == 0)
                    {
                        _logger.LogDebug(
                            "Renewal order {OrderId}: replacement voucher {VoucherId} claimed by another instance, skipping",
                            orderId, stock.Id);
                        continue;
                    }

                    // The replacement is in the customer's hands NOW, and this is the winning side of
                    // the CAS - which is the only reason the records below describe something that
                    // happened rather than something we intended. Past `claimed == 0` the paper really
                    // moved; above it, nothing did.
                    //
                    // Two facts, two owners: the custody movement, and the status change the same
                    // statement made. Neither derives the other - you cannot tell who received a ticket
                    // from a status row, and you cannot tell it changed state from a custody row - so
                    // writing one would lose half of what happened.
                    //
                    // amount_uah is deliberately NOT written. What the customer paid for this is
                    // order_line_items.line_total, and the line already carries it; the movement points at
                    // the line through line_item_id, so a copy here would be a second thing to keep in
                    // step. The column exists for movements with no order behind them.
                    _context.VoucherMovements.Add(new VoucherMovement
                    {
                        Id = Guid.CreateVersion7(),
                        Kind = VoucherMovementKinds.ReplaceOut,
                        VoucherId = stock.Id,
                        FromLocation = VoucherLocations.Pool,
                        ToLocation = VoucherLocations.Customer,
                        OrderId = orderId,
                        LineItemId = line.Id,
                    ActorKind = VoucherActorKinds.User,
                        ActorUserId = order.UserId,
                        OccurredAtUtc = now
                    });

                    _context.VoucherEvents.Add(new VoucherEvent
                    {
                        Id = Guid.CreateVersion7(),
                        VoucherId = stock.Id,
                        EventType = VoucherEventTypes.StatusChanged,
                        // Taken from the CAS itself: it sets 'Assigned' WHERE status = 'Available'.
                        FromStatus = nameof(VoucherStatus.Available),
                        ToStatus = nameof(VoucherStatus.Assigned),
                        ExternalRef = orderId.ToString(),
                    ActorKind = VoucherActorKinds.User,
                        ActorUserId = order.UserId,
                        OccurredAtUtc = now
                    });

                    // The old voucher is ours again and goes back into the sellable pool: the customer walked away with a
                    // different one, so this is stock again and whoever claims it next pays for it. Ownership
                    // and the stale order link are cleared so it cannot resurface in the previous customer's
                    // wallet (GetUserPurchases reads vouchers through fulfillments) or read as sold stock.
                    // Best-effort - 0 rows means it was already released or reassigned, an acceptable end
                    // state since the replacement is already in the customer's hands.
                    var released = await TryReleaseReplacedVoucherAsync(source.Id, order.UserId, cancellationToken);

                    // Gated on the release itself, for the same reason the claim above is gated on its
                    // own: 0 rows means the paper did not move, so recording a `returned` would put a
                    // voucher in the ledger's custody history that never left anyone.
                    if (released > 0)
                    {
                        _context.VoucherMovements.Add(new VoucherMovement
                        {
                            Id = Guid.CreateVersion7(),
                            Kind = VoucherMovementKinds.Returned,
                            VoucherId = source.Id,
                            FromLocation = VoucherLocations.Customer,
                            ToLocation = VoucherLocations.Pool,
                            OrderId = orderId,
                            LineItemId = line.Id,
                    ActorKind = VoucherActorKinds.User,
                            ActorUserId = order.UserId,
                            OccurredAtUtc = now
                        });

                        _context.VoucherEvents.Add(new VoucherEvent
                        {
                            Id = Guid.CreateVersion7(),
                            VoucherId = source.Id,
                            EventType = VoucherEventTypes.StatusChanged,
                            // Also from the CAS: it sets 'Available' on the customer's voucher.
                            FromStatus = nameof(VoucherStatus.Assigned),
                            ToStatus = nameof(VoucherStatus.Available),
                            ExternalRef = orderId.ToString(),
                    ActorKind = VoucherActorKinds.User,
                            ActorUserId = order.UserId,
                            OccurredAtUtc = now
                        });
                    }

                    usedStockIds.Add(stock.Id);
                    line.PreviousCustomerExpiration = source.CustomerExpirationDate;
                    // The customer-facing expiry history must show what the customer actually holds: the
                    // promise they paid for. The replacement voucher's own customer date is NOT it — we stamp
                    // the promise onto the voucher at claim time, and any longer real life above that is a
                    // hidden reserve the customer can extend again later.
                    line.NewCustomerExpiration = promisedExpiration;
                    // Same as the extend branch: the fee is revenue. The replacement's cost stays what we
                    // paid for it, which is also what the below-cost guard measured it against.
                    _context.Fulfillments.Add(new Fulfillment
                    {
                        OrderId = orderId,
                        VoucherId = stock.Id,
                        LineItemId = line.Id,
                        FulfilledAtUtc = now
                    });
                    fulfilledCount++;

                    _logger.LogInformation(
                        "Renewal order {OrderId}: replaced expired voucher {SourceId} with stock {StockId} (term {TermCode})",
                        orderId, source.Id, stock.Id, line.TermCode);
                }
            }

            await _context.SaveChangesAsync(cancellationToken);

            if (fulfilledCount >= renewalLines.Count)
            {
                var updated = await TryMarkOrderFulfilledAsync(orderId, cancellationToken);
                if (updated > 0)
                {
                    _logger.LogInformation("Renewal order {OrderId} fully fulfilled", orderId);

                    // jsonb containment dedup, matching the buy-fuel OrderFulfilled emit.
                    var orderProbe = System.Text.Json.JsonSerializer.Serialize(new { orderId });
                    var hasFulfilledEvent = await _context.OutboxEvents
                        .AnyAsync(e => e.EventType == OutboxEventType.OrderFulfilled
                                    && EF.Functions.JsonContains(e.Payload, orderProbe),
                                  cancellationToken);

                    if (!hasFulfilledEvent)
                    {
                        _context.OutboxEvents.Add(new OutboxEvent
                        {
                            EventType = OutboxEventType.OrderFulfilled,
                            Payload = System.Text.Json.JsonSerializer.Serialize(new
                            {
                                orderId,
                                userId = order.UserId,
                                fulfilledAt = DateTime.UtcNow
                            }),
                            Processed = false,
                            CreatedAtUtc = DateTime.UtcNow
                        });
                        await _context.SaveChangesAsync(cancellationToken);
                    }
                }
            }
            else if (fulfilledCount > 0)
            {
                var orderToUpdate = await _context.Orders
                    .FirstOrDefaultAsync(o => o.Id == orderId &&
                               (o.Status == OrderStatus.PendingFulfillment || o.Status == OrderStatus.PartiallyFulfilled),
                               cancellationToken);

                if (orderToUpdate != null)
                {
                    orderToUpdate.Status = OrderStatus.PartiallyFulfilled;
                    orderToUpdate.FulfilledAtUtc = null;
                    // Keep the first partial timestamp so the auto-refund grace is measured from
                    // then, not from every re-run.
                    orderToUpdate.PartiallyFulfilledSinceUtc ??= DateTime.UtcNow;
                    orderToUpdate.UpdatedAtUtc = DateTime.UtcNow;
                    _context.Orders.Update(orderToUpdate);
                    await _context.SaveChangesAsync(cancellationToken);

                    _logger.LogInformation(
                        "Renewal order {OrderId} partially fulfilled: {Fulfilled}/{Total} items",
                        orderId, fulfilledCount, renewalLines.Count);

                    shouldAutoRefund = true;
                }
            }
            // fulfilledCount == 0: leave PendingFulfillment. The per-minute renewal backfill retries
            // and the no-stock alert has already been raised for each missing line.

            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            throw;
        }

        // Same path as buy-fuel partials: refunds only the unfulfilled lines, gated by the
        // AutoRefund runtime setting and its grace window (this realises the "auto-refund that row"
        // decision for a post-payment no-stock replace).
        if (shouldAutoRefund)
        {
            await TryAutoRefundAsync(orderId, cancellationToken);
        }
    }

    /// <summary>
    /// Finds open renewal orders (at least one still-unfulfilled item) and re-runs their fulfilment.
    /// Folded into <see cref="ProcessPendingOrdersAsync"/> so the per-minute API job is the safety
    /// net when a webhook enqueue was lost or stock only arrived later.
    /// </summary>
    private async Task ProcessOpenRenewalOrdersBackfillAsync(CancellationToken cancellationToken)
    {
// "Has a renewal line we have not delivered yet" - the same question the old
        // `VoucherRenewalItems.Any(i => i.FulfilledVoucherId == null)` asked, asked of the line and
        // its fulfilment instead. NOT negated by the global released-filter: a released fulfilment
        // still delivered the term, so the line is not owed again.
        var openRenewalOrderIds = await _context.Orders
            .Where(o => (o.Status == OrderStatus.PendingFulfillment || o.Status == OrderStatus.PartiallyFulfilled)
                         && o.Kind == OrderKind.Renewal
                         && _context.OrderLineItems
                             .Where(l => l.OrderId == o.Id && l.SourceVoucherId != null)
                             .Any(l => !_context.Fulfillments
                                 .IgnoreQueryFilters()
                                 .Any(f => f.LineItemId == l.Id)))
            .OrderBy(o => o.CreatedAtUtc)
            .Select(o => o.Id)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (openRenewalOrderIds.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Backfilling {Count} open renewal orders", openRenewalOrderIds.Count);

        foreach (var orderId in openRenewalOrderIds)
        {
            try
            {
                await ProcessRenewalOrderAsync(orderId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to backfill renewal order {OrderId}", orderId);
            }
        }
    }


    private async Task<FuelVoucher?> FindReplacementVoucherAsync(
        FuelVoucher source,
        DateOnly promisedExpiration,
        List<Guid> usedStockIds,
        bool isTestAccount,
        CancellationToken cancellationToken)
    {
        // Same brand/fuel/nominal as the source, valid at least until the promise we owe the
        // customer (their leftover term + what they just paid for), not already claimed in this run.
        // Oldest-eligible first, so the SHORTEST qualifying voucher goes out and Fuel Flow's near-expiry
        // stock is preserved for the tiers that actually need it — a shorter voucher that still covers the
        // promise is strictly better than a longer one. A QA account draws only from test stock and a real
        // account only from real stock, re-asserted atomically at the claim (TryAssignReplacementVoucherAsync).
        // AsNoTracking for the same reason as FindMatchingVoucherAsync (#246): a decision whose row is
        // claimed by a raw UPDATE two lines later, never a write in its own right.
        var pool = DataKinds.ForPool(isTestAccount);

        return await _context.FuelVouchers
            .AsNoTracking()
            .Where(v => v.Status == VoucherStatus.Available
                     && v.Brand.ToLower() == source.Brand.ToLower()
                     && v.FuelTypeId == source.FuelTypeId
                     && v.Liters == source.Liters
                     && v.DataKind == pool
                     && v.SupplierExpirationDate >= promisedExpiration
                     && !usedStockIds.Contains(v.Id))
            .OrderBy(v => v.SupplierExpirationDate)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Atomic extension with compare-and-set: moves the customer expiry to <paramref name="newExpiration"/>
    /// only while the row still holds the dates we priced against AND the new date still fits inside the
    /// supplier's real term. Returns rows affected (0 or 1), so 0 means "someone beat us, or the ceiling
    /// moved" rather than a silent success.
    /// </summary>
    protected internal virtual async Task<int> TryExtendVoucherAsync(
        Guid voucherId,
        Guid userId,
        DateOnly expectedCustomerExpiration,
        DateOnly providerExpiration,
        DateOnly newExpiration,
        CancellationToken cancellationToken)
    {
        return await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "fuel_vouchers" SET customer_expiration_date = {newExpiration}, updated_at_utc = {DateTime.UtcNow} WHERE id = {voucherId} AND assigned_to_user_id = {userId} AND status = 'Assigned' AND customer_expiration_date = {expectedCustomerExpiration} AND {newExpiration} <= supplier_expiration_date""",
            cancellationToken);
    }

    /// <summary>Atomic replacement claim: assign an Available voucher to the customer only if it is
    /// still valid at least until <paramref name="promisedExpiration"/> (the source's leftover term plus the
    /// term just paid for). The expiry gate is
    /// re-checked here, not just at select time, so an admin edit between SELECT and claim cannot
    /// hand out an under-term voucher. Returns rows affected (0 or 1).</summary>
    protected internal virtual async Task<int> TryAssignReplacementVoucherAsync(
        Guid voucherId, Guid userId, Guid? legalEntityId, DateOnly promisedExpiration, Guid orderId, CancellationToken cancellationToken)
    {
        // The renewal order owns the replacement, so order_id is written in the same atomic claim
        // as the assignment rather than inferred later from the Fulfillment row.
        //
        // customer_expiration_date is stamped with the PROMISE (what the customer paid for on top of
        // what they already held), not the voucher's own customer date. The voucher is chosen with
        // supplier_expiration_date >= promise, so the promise always fits and never needs clamping —
        // whatever real life is left above it is a hidden reserve the customer can extend again.
        //
        // Same QA/test segregation as the buy claim: the replacement must come from the owner's pool
        // (test for a QA account, real otherwise), re-asserted atomically here against the owning user.
        return await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "fuel_vouchers" SET status = 'Assigned', assigned_to_user_id = {userId}, legal_entity_id = {legalEntityId}, worker_user_id = NULL, order_id = {orderId}, customer_expiration_date = {promisedExpiration}, updated_at_utc = {DateTime.UtcNow} WHERE id = {voucherId} AND status = 'Available' AND supplier_expiration_date >= {promisedExpiration} AND data_kind = (SELECT CASE WHEN u.is_qa_account THEN 'qa' ELSE 'real' END FROM "users" u WHERE u.id = {userId})""",
            cancellationToken);
    }

    /// <summary>
    /// Puts a replaced customer's voucher back into the sellable pool. The customer is done with it — we
    /// issued them a different voucher — so ownership, the legal-entity stamp and any worker hand-off are
    /// cleared and the status returns to <c>Available</c>: it is Fuel Flow's stock again, and the next
    /// customer to claim it (possibly the same one) pays for it again, so there is no double revenue.
    /// <para>
    /// Its <c>order_id</c> is deliberately KEPT, still pointing at the purchase that delivered it. That link
    /// records where this paper originally came from, and re-homing it to the renewal would rewrite that
    /// (pinned by <c>VoucherOrderOwnershipIntegrationTests</c>). It is also harmless as stock: the wallet
    /// reads vouchers through <c>Fulfillments</c>, and the next claim overwrites it anyway.
    /// </para>
    /// <para>
    /// The <see cref="Fulfillment"/> row — and through its LineItemId, the renewal line that bought the
    /// replacement — is kept too: together they are the audit chain that still explains where this
    /// voucher went before. What must NOT happen is the previous customer still seeing it —
    /// <c>GetUserPurchases</c> only returns vouchers the caller actually owns, which is what keeps a
    /// recycled voucher out of the wallet it came from.
    /// </para>
    /// <para>
    /// It does re-enter the operator's «Заміна талонів» attention list once it nears expiry, which is right:
    /// it is stock that is about to lapse, and swapping it with the supplier is one way to stop that.
    /// </para>
    /// </summary>
    protected internal virtual async Task<int> TryReleaseReplacedVoucherAsync(
        Guid voucherId, Guid userId, CancellationToken cancellationToken)
    {
        return await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "fuel_vouchers" SET status = 'Available', assigned_to_user_id = NULL, legal_entity_id = NULL, worker_user_id = NULL, updated_at_utc = {DateTime.UtcNow} WHERE id = {voucherId} AND assigned_to_user_id = {userId} AND status = 'Assigned'""",
            cancellationToken);
    }
}


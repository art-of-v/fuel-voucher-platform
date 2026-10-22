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

        // Collected here and sent after the transaction is closed - see the same list in
        // AssignVouchersToOrderAsync for why the alert cannot be sent inside the transaction.
        var pendingStockAlerts = new List<PendingStockAlert>();

        async Task ApplyAsync()
        {
            // The strategy may replay the body, so the collected alerts start empty: a list that
            // survived the replay would announce the same shortfall twice.
            pendingStockAlerts.Clear();

            if (_context.Database.IsRelational())
            {
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
                return;
            }

            // The same money question the buy-fuel path asks, for the same reason: a renewal
            // extends a customer's fuel, and handing out an extension is handing out fuel.
            if (!await OrderPayment.HasCapturedPaymentAsync(_context, order.Id, cancellationToken))
            {
                _logger.LogError(
                    "Renewal order {OrderId} is eligible for fulfilment but has no captured payment; no vouchers were extended",
                    orderId);

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

            var today = DateOnly.FromDateTime(SystemClock.UtcNow);
            var now = SystemClock.UtcNow;
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
                    // Writing a customer date past the real term would promise validity the Brand may
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
                            ? source.FuelTypeId.ToString().ToString()
                            : $"{fuelTypeName} ({source.Brand.ToUpperInvariant()}, {source.Liters:0.##} L)";

                        _logger.LogWarning(
                            "Renewal order {OrderId}: no replacement stock for {FuelLabel} valid until {PromisedExpiration}",
                            orderId, fuelLabel, promisedExpiration);

                        // Collected, not sent: the alert goes out after the transaction is closed.
                        pendingStockAlerts.Add(new PendingStockAlert(orderId, fuelLabel, 0, 1));
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
                                fulfilledAt = SystemClock.UtcNow
                            }),
                            Processed = false,
                            CreatedAtUtc = SystemClock.UtcNow
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
                    orderToUpdate.PartiallyFulfilledSinceUtc ??= SystemClock.UtcNow;
                    orderToUpdate.UpdatedAtUtc = SystemClock.UtcNow;
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
        }

        // The transaction runs through ReplaySafeTransaction, so the execution strategy may replay
        // it: a transient failure retries the body rather than losing the renewal.
        await _context.ExecuteReplaySafeAsync(_ => ApplyAsync(), cancellationToken);

        // The transaction is closed. The alerts go out now.
        foreach (var alert in pendingStockAlerts)
        {
            await _notifications.OrderUnfulfillableAsync(
                alert.OrderId, alert.FuelLabel, alert.Assigned, alert.Needed, cancellationToken);
        }

        // Same path as buy-fuel partials: refunds only the unfulfilled lines, gated on the
        // AutoRefund runtime setting and its grace window (this realises the "auto-refund that row"
        // decision for a post-payment no-stock replace).
        if (shouldAutoRefund)
        {
            await TryAutoRefundAsync(orderId, cancellationToken);
        }
    }
}

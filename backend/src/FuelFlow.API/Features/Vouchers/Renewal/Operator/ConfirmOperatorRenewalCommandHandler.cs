using System.Text.Json;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Vouchers.Renewal.Checkout;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Features.Vouchers.Events;
using FuelFlow.Features.Vouchers.Movements;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FuelFlow.Features.Vouchers.Renewal.Operator;

/// <summary>
/// An operator records, in the admin app, a renewal they performed off-platform on a CUSTOMER's
/// voucher. <see cref="SurchargeUah"/> is the доплата the customer already paid the operator (0 = free
/// compensation) — recorded for audit only, never a Monobank charge and never a stock reprice. Acting
/// user fields are filled from the caller's claims by the controller, not the client.
/// </summary>
public sealed class ConfirmOperatorRenewalCommand
{
    public Guid VoucherId { get; set; }
    public string TermCode { get; set; } = string.Empty;
    public decimal SurchargeUah { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateOnly? InvoiceDate { get; set; }

    public Guid ActingUserId { get; set; }
    public string? ActingUserName { get; set; }
}

/// <summary>Outcome of a confirmed operator renewal — enough for the admin UI to render the result card.</summary>
public sealed class ConfirmOperatorRenewalResult
{
    public string Branch { get; set; } = string.Empty;
    public Guid VoucherId { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
    public Guid? ReplacementVoucherId { get; set; }
    public string? ReplacementVoucherNumber { get; set; }
    public DateOnly OldExpiration { get; set; }
    public DateOnly NewExpiration { get; set; }
    public string TermCode { get; set; } = string.Empty;
    public decimal SurchargeUah { get; set; }
    public Guid CustomerUserId { get; set; }
    public string? CustomerName { get; set; }
}

/// <summary>
/// Applies the two-branch renewal (same rules as the mobile flow, via <see cref="VoucherRenewalEligibility"/>)
/// to a customer's voucher on an operator's behalf, inside one DB transaction with tracked EF updates.
/// Unlike <see cref="RenewalCheckoutCommandHandler"/> there is no payment, no device signature and no
/// trigger-window gate — the operator acts deliberately. Business rejections surface as
/// <see cref="VoucherRenewalException"/> with a stable code (controller maps <c>not_found</c>→404,
/// <c>no_stock</c>→409, the rest→400).
/// </summary>
public sealed class ConfirmOperatorRenewalCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly AuditEventService _events;

    public ConfirmOperatorRenewalCommandHandler(ApplicationDbContext context, AuditEventService events)
    {
        _context = context;
        _events = events;
    }

    public async Task<ConfirmOperatorRenewalResult> HandleAsync(
        ConfirmOperatorRenewalCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!VoucherRenewalTerms.TryFromCode(command.TermCode, out var term))
            throw new VoucherRenewalException("unknown_term", $"Unknown renewal term '{command.TermCode}'.");

        if (command.SurchargeUah < 0)
            throw new VoucherRenewalException("invalid_surcharge", "Surcharge must be zero or positive.");

        // Serialize per voucher, the way FulfillmentService serializes per order (see the
        // pg_advisory_xact_lock there). Without it two operators confirming the same voucher at the
        // same moment both read the same old expiration, both compute a term from it, and both write
        // a settlement row - so the customer is charged twice for one renewal.
        //
        // A unique index on operator_voucher_renewals.voucher_id would be the obvious fix and is the
        // wrong one: the Extend branch keeps the SAME voucher as the customer's active one, so a
        // voucher may legitimately be extended several times over its life. The constraint that would
        // reject the duplicate is also the one that would break the business rule.
        //
        // Taken BEFORE the load. A lock acquired after the read would serialize the writers but on
        // data they had already captured - the second operator would queue, wake up, and still extend
        // from the expiration it read before the first one committed.
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext('operator-renewal-voucher'), hashtext({command.VoucherId.ToString()}))",
            cancellationToken);

        // Tracked load (NoTracking is the DbContext default — mutations would silently no-op otherwise).
        // IgnoreQueryFilters so a soft-deleted row is distinguishable (→ not_found) rather than invisible.
        var voucher = await _context.FuelVouchers
            .AsTracking()
            .IgnoreQueryFilters()
            .Include(v => v.AssignedToUser)
            .FirstOrDefaultAsync(v => v.Id == command.VoucherId, cancellationToken);

        if (voucher is null || voucher.IsDeleted)
            throw new VoucherRenewalException("not_found", "Voucher not found.");

        if (voucher.AssignedToUserId is null)
            throw new VoucherRenewalException("not_customer_voucher", "Only a customer-owned voucher can be renewed here; this is unassigned stock.");

        if (!VoucherRenewalEligibility.IsRenewableStatus(voucher.Status))
            throw new VoucherRenewalException("not_renewable", $"A voucher in status '{voucher.Status}' cannot be renewed.");

        var today = DateOnly.FromDateTime(SystemClock.UtcNow);
        var branch = voucher.CustomerExpirationDate >= today
            ? VoucherRenewalBranch.Extend
            : VoucherRenewalBranch.Replace;

        // The ceiling, and the same rule the self-serve path applies at
        // RenewalCheckoutCommandHandler.cs:135. `NewExpirationForExtend` has no supplier parameter by
        // construction - its own contract says the caller checks the result via `CanExtend` - and this
        // caller was not doing it.
        //
        // What it cost: an operator could choose a term the supplier's paper cannot cover, and the
        // promise was stamped straight onto the voucher. The customer pays for fuel that stops
        // working at the pump on the provider's date, days or weeks before the date we told them.
        // Nothing caught it - there was no database constraint, and fulfilment, which does check,
        // simply took the other branch and quietly replaced the voucher instead.
        //
        // Refused rather than silently downgraded to Replace. Checkout refuses for the same reason:
        // the operator picked this term, and handing them a different operation without saying so is
        // the failure mode this whole codebase argues against. The message is the self-serve one so
        // the two paths read identically.
        if (branch == VoucherRenewalBranch.Extend &&
            !VoucherRenewalEligibility.CanExtend(
                voucher.CustomerExpirationDate, voucher.SupplierExpirationDate, term))
            throw new VoucherRenewalException("provider_term_exhausted",
                $"The '{command.TermCode}' term runs past this voucher's supplier validity " +
                $"(valid to {voucher.SupplierExpirationDate:yyyy-MM-dd}). Renew it as a replacement, or pick a shorter term.");

        var oldExpiration = voucher.CustomerExpirationDate;
        var now = SystemClock.UtcNow;

        // Captured before any mutation: the replace branch releases the voucher's ownership below, and
        // the audit row, the summary and the customer's name all still need to know whose it was.
        var customerUserId = voucher.AssignedToUserId.Value;
        var customerLegalEntityId = voucher.LegalEntityId;
        var customerName = ComposeName(voucher.AssignedToUser);

        Guid? replacementVoucherId = null;
        string? replacementVoucherNumber = null;
        FuelVoucher? replacementStock = null;
        DateOnly newExpiration;

        if (branch == VoucherRenewalBranch.Extend)
        {
            newExpiration = VoucherRenewalEligibility.NewExpirationForExtend(oldExpiration, term);
            voucher.CustomerExpirationDate = newExpiration;
            voucher.Status = VoucherStatus.Assigned; // keep it the customer's active voucher
            voucher.UpdatedAtUtc = now;

            // D-13. `Extended`, exactly as the self-serve path records it, with the pair — the old
            // customer expiry is overwritten on the line above, so without this the value before the
            // renewal is unreconstructible. That is the whole reason this event type exists.
            //
            // No `StatusChanged`: `IsRenewableStatus` admits `Assigned`, so the voucher's status may well
            // be `Assigned` before and after, and ck_voucher_events_status_changed would refuse
            // from == to. An extend that changes only the term changed no status, so claiming one would
            // be an invention rather than a record.
            _context.VoucherEvents.Add(new VoucherEvent
            {
                Id = Guid.CreateVersion7(),
                VoucherId = voucher.Id,
                EventType = VoucherEventTypes.Extended,
                PreviousCustomerExpiration = oldExpiration,
                NewCustomerExpiration = newExpiration,
                ActorKind = VoucherActorKinds.User,
                ActorUserId = command.ActingUserId,
                OccurredAtUtc = now
            });
        }
        else
        {
            var promisedExpiration = VoucherRenewalEligibility.PromisedExpirationForReplace(
                today, voucher.CustomerExpirationDate, term);
            var providerSlug = voucher.Brand;

            // Shortest qualifying voucher first, mirroring FulfillmentService.FindReplacementVoucherAsync:
            // the floor is what we owe the customer, and handing out a shorter voucher that still covers
            // that preserves Fuel Flow's near-expiry stock for the tiers that need it.
            var stock = await _context.FuelVouchers
                .AsTracking()
                .Where(v => v.Status == VoucherStatus.Available
                         && v.Brand == providerSlug
                         && v.FuelTypeId == voucher.FuelTypeId
                         && v.Liters == voucher.Liters
                         && v.SupplierExpirationDate >= promisedExpiration)
                .OrderBy(v => v.SupplierExpirationDate)
                .FirstOrDefaultAsync(cancellationToken);

            if (stock is null)
                throw new VoucherRenewalException(
                    "no_stock",
                    "No replacement voucher is available in stock for this brand, fuel and volume. Import stock first.");

            stock.Status = VoucherStatus.Assigned;
            stock.AssignedToUserId = customerUserId;
            stock.LegalEntityId = customerLegalEntityId;
            stock.WorkerUserId = null;
            // Stamp the promise, not the stock voucher's own customer date — same rule as the self-serve
            // replace path, so the customer sees what they paid for and the extra real life is a reserve.
            stock.CustomerExpirationDate = promisedExpiration;
            stock.UpdatedAtUtc = now;

            // D-13. The operator path's mirror of the self-serve renewal's two movements, and it is the
            // same pair for the same reasons: the replacement leaves the pool for the customer, and the
            // old paper comes back. `replace_out` rather than `issued` because the reason differs, which
            // is the same split the self-serve writer uses for the identical physical fact.
            //
            // The rows themselves are written further down, once the order and the line they settle
            // exist - a movement names the order and the line it settled, and those are created after
            // this branch knows what the renewal is.

            _context.VoucherEvents.Add(new VoucherEvent
            {
                Id = Guid.CreateVersion7(),
                VoucherId = stock.Id,
                EventType = VoucherEventTypes.StatusChanged,
                // From the statuses the statements below set: the claim is Available -> Assigned.
                FromStatus = nameof(VoucherStatus.Available),
                ToStatus = nameof(VoucherStatus.Assigned),
                ActorKind = VoucherActorKinds.User,
                ActorUserId = command.ActingUserId,
                OccurredAtUtc = now
            });

            // Same release as the self-serve replace path: the voucher goes back into the sellable pool
            // (ownership, entity and any worker hand-off cleared) so whoever claims it next pays for it.
            // Its order_id is kept on purpose — that is the purchase this paper came from, and re-homing it
            // to the renewal order would rewrite the fuel's origin.
            var releasedFromStatus = voucher.Status;
            voucher.Status = VoucherStatus.Available;
            voucher.AssignedToUserId = null;
            voucher.LegalEntityId = null;
            voucher.WorkerUserId = null;
            voucher.UpdatedAtUtc = now;

            // And the customer's half of the round trip: the paper they handed over is stock again.
            // The movement that says so is written further down, with the order and the line it
            // settles - and it names the replacement, because the retired paper's row is where the
            // trade is recorded (planning #319).

            // `releasedFromStatus` rather than a literal: the voucher may be `Assigned` or `Expired`
            // (both are renewable), and the history has to say which one it actually was.
            _context.VoucherEvents.Add(new VoucherEvent
            {
                Id = Guid.CreateVersion7(),
                VoucherId = voucher.Id,
                EventType = VoucherEventTypes.StatusChanged,
                FromStatus = releasedFromStatus.ToString(),
                ToStatus = nameof(VoucherStatus.Available),
                ActorKind = VoucherActorKinds.User,
                ActorUserId = command.ActingUserId,
                OccurredAtUtc = now
            });

            replacementVoucherId = stock.Id;
            replacementVoucherNumber = stock.VoucherNumber;
            replacementStock = stock;
            newExpiration = promisedExpiration;

            // A replace hands the customer a voucher that costs us real money, so the surcharge is a
            // sale price and has to clear the stock's cost NET of the voucher coming back to us — the
            // customer's own voucher is not scrap, it re-enters the sellable pool, and both sides are
            // valued at what we paid. Judging the fee alone refused renewals that made money.
            // Extends are exempt by construction — no asset leaves.
            if (RenewalMargin.ForReplacement(
                    command.SurchargeUah, voucher.Liters, stock.CostPerLiter, voucher.CostPerLiter)
                == RenewalMarginVerdict.BelowCost)
            {
                var fuelType = await _context.FuelTypes
                    .AsNoTracking()
                    .FirstOrDefaultAsync(f => f.Id == voucher.FuelTypeId, cancellationToken);

                // Same standing opt-in the fuel panel offers: a deliberate loss-leader, entered by a
                // person on purpose, is a legitimate way to clear near-expiry stock.
                if (fuelType?.AllowBelowCost != true)
                {
                    var shortfall = RenewalMargin.ShortfallUah(
                        command.SurchargeUah, voucher.Liters, stock.CostPerLiter, voucher.CostPerLiter);
                    throw new VoucherRenewalException(
                        "below_cost",
                        $"Renewal surcharge is below the replacement voucher's cost by {shortfall:F2} UAH. "
                        + "Enable the below-cost opt-in for this fuel if this is deliberate.",
                        // The figure travels as data and not only as prose. The admin localises this
                        // refusal, and a translated sentence that swallowed the shortfall would leave
                        // the operator unable to tell a rounding artefact from a real loss.
                        new Dictionary<string, decimal> { ["shortfallUah"] = shortfall });
                }
            }
        }

        // A renewal hands fuel to the customer and collects a surcharge, so it gets an order like
        // every other handover - exactly what the self-service flow already does. Without one the
        // replacement voucher would sit in someone's hands with no order explaining where it came
        // from, and the surcharge would have no document. Price is the surcharge actually paid, so
        // the renewal shows up in revenue instead of vanishing; Kind marks it as a renewal rather
        // than a fuel purchase.
        var branchCode = branch == VoucherRenewalBranch.Extend ? "extend" : "replace";

        var renewalLine = new OrderLineItem
        {
            Id = Guid.CreateVersion7(),
            Brand = voucher.Brand,
            // The brand's key beside its name - the voucher's own key, because the line is
            // about the same fuel the customer brings back.
            BrandId = voucher.BrandId,
            FuelTypeId = voucher.FuelTypeId,
            Liters = voucher.Liters,
            Quantity = 1,
            UnitPrice = command.SurchargeUah,
            LineTotal = command.SurchargeUah,
            // The renewal's INTENT, on the line - the same columns the self-serve path writes
            // (D-13's steps 2 and 3). The operator's renewal used to keep all of this in a table
            // of its own; the line is where a renewal's facts live, and the two paths now read the
            // same way.
            TermCode = term.Code(),
            RenewalBranch = branchCode,
            SourceVoucherId = voucher.Id,
            // The pair, because the customer's own expiry is overwritten in place by the renewal,
            // so "what was it before" exists nowhere else.
            PreviousCustomerExpiration = oldExpiration,
            NewCustomerExpiration = newExpiration
        };

        var renewalOrder = new Order
        {
            Id = Guid.CreateVersion7(),
            UserId = customerUserId,
            LegalEntityId = customerLegalEntityId,
            Price = command.SurchargeUah,
            Kind = OrderKind.Renewal,
            Status = OrderStatus.Fulfilled,
            FulfilledAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            // The brand's накладна for the off-platform renewal: the document the customer paid
            // against. A self-serve order has none - the acquirer's invoice is monobank_invoice_id.
            ExternalInvoiceNumber = string.IsNullOrWhiteSpace(command.InvoiceNumber) ? null : command.InvoiceNumber.Trim(),
            ExternalInvoiceDate = command.InvoiceDate,
            LineItems = new List<OrderLineItem> { renewalLine }
        };

        _context.Orders.Add(renewalOrder);
        _context.Fulfillments.Add(new Fulfillment
        {
            OrderId = renewalOrder.Id,
            VoucherId = replacementVoucherId ?? voucher.Id,
            FulfilledAtUtc = now
        });

        // Stock leaves the warehouse and belongs to this renewal order. On the Extend branch the
        // voucher keeps the order that originally delivered it - it is the same voucher, only its
        // end date moved.
        if (replacementStock is not null)
            replacementStock.OrderId = renewalOrder.Id;

        // The custody rows, now that the order and the line they settle exist: the replacement
        // leaves the pool for the customer, and the paper they handed over comes back. Both name
        // the order and the line, so "which renewal was this" is answerable from the ledger alone.
        if (replacementStock is not null)
        {
            _context.VoucherMovements.Add(new VoucherMovement
            {
                Id = Guid.CreateVersion7(),
                Kind = VoucherMovementKinds.ReplaceOut,
                VoucherId = replacementStock.Id,
                FromLocation = VoucherLocations.Pool,
                ToLocation = VoucherLocations.Customer,
                ActorKind = VoucherActorKinds.User,
                ActorUserId = command.ActingUserId,
                OccurredAtUtc = now,
                OrderId = renewalOrder.Id,
                LineItemId = renewalLine.Id
            });

            // The retired paper's own row names its successor: the customer's old voucher was
            // replaced by the fresh one (planning #319).
            _context.VoucherMovements.Add(new VoucherMovement
            {
                Id = Guid.CreateVersion7(),
                Kind = VoucherMovementKinds.Returned,
                VoucherId = voucher.Id,
                FromLocation = VoucherLocations.Customer,
                ToLocation = VoucherLocations.Pool,
                ActorKind = VoucherActorKinds.User,
                ActorUserId = command.ActingUserId,
                OccurredAtUtc = now,
                OrderId = renewalOrder.Id,
                LineItemId = renewalLine.Id,
                ReplacedByVoucherId = replacementVoucherId
            });
        }

        var summary = branch == VoucherRenewalBranch.Extend
            ? $"Operator extended customer voucher {voucher.VoucherNumber} ({term.Code()}) → {newExpiration:yyyy-MM-dd}, surcharge ₴{command.SurchargeUah}"
            : $"Operator replaced lapsed customer voucher {voucher.VoucherNumber} from stock {replacementVoucherNumber} ({term.Code()}), surcharge ₴{command.SurchargeUah}";

        // old_value / new_value are jsonb columns — pass serialized JSON, never a bare string.
        var newValueJson = JsonSerializer.Serialize(new
        {
            branch = branchCode,
            voucherId = voucher.Id,
            replacementVoucherId,
            termCode = term.Code(),
            oldExpiration = oldExpiration.ToString("yyyy-MM-dd"),
            newExpiration = newExpiration.ToString("yyyy-MM-dd"),
            surchargeUah = command.SurchargeUah,
            customerUserId
        });

        // Tracked inside this transaction, not saved on its own: the renewal and the row that
        // records it commit together, or the transaction rolls both back (planning #533).
        _events.RecordEvent(
            aggregateType: "Voucher",
            aggregateId: voucher.Id.ToString(),
            eventType: "VoucherRenewedByOperator",
            oldValue: null,
            newValue: newValueJson,
            changedByUserId: command.ActingUserId,
            changedByUserName: command.ActingUserName,
            summary: summary,
            providerId: voucher.Brand);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ix_order_line_items_renewal_once"
        })
        {
            // The database refused the row, because this voucher already has a renewal to this date.
            // The advisory lock above means the ordinary double-click never gets here - it is the
            // backstop for the case where the same renewal is recorded twice by two paths at once.
            // The answer is the same answer the operator gets for any other refusal: what happened,
            // in words, and not a 500.
            throw new VoucherRenewalException(
                "already_renewed",
                "This voucher already has a renewal to that date.");
        }

        await transaction.CommitAsync(cancellationToken);

        return new ConfirmOperatorRenewalResult
        {
            Branch = branchCode,
            VoucherId = voucher.Id,
            VoucherNumber = voucher.VoucherNumber,
            ReplacementVoucherId = replacementVoucherId,
            ReplacementVoucherNumber = replacementVoucherNumber,
            OldExpiration = oldExpiration,
            NewExpiration = newExpiration,
            TermCode = term.Code(),
            SurchargeUah = command.SurchargeUah,
            CustomerUserId = customerUserId,
            CustomerName = customerName
        };
    }

    private static string? ComposeName(SharedKernel.Domain.User? user)
    {
        if (user is null) return null;
        var name = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrEmpty(name) ? user.PhoneNumber : name;
    }
}

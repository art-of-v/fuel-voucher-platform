using FuelFlow.API.BackgroundJobs.Models;
using FuelFlow.API.Features.Orders.SharedServices;
using FuelFlow.Features.Company.SharedModels;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Movements;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Company.GiftVouchers;

public sealed record GiftVouchersCommand(Guid OwnerUserId, Guid WorkerUserId, IReadOnlyList<Guid> VoucherIds, Guid? LegalEntityId = null);

public sealed record GiftVouchersResult(string Status, int GiftedCount = 0, string? ErrorMessage = null, Guid? IssuanceOrderId = null);

public sealed class GiftVouchersCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly FuelFlowMetrics _metrics;

    public GiftVouchersCommandHandler(ApplicationDbContext context, FuelFlowMetrics metrics)
    {
        _context = context;
        _metrics = metrics;
    }

    public async Task<GiftVouchersResult> HandleAsync(GiftVouchersCommand command, CancellationToken cancellationToken = default)
    {
        if (command.VoucherIds.Count == 0)
        {
            return new GiftVouchersResult("EmptyVoucherList", ErrorMessage: "At least one voucher must be provided.");
        }

        var resolution = await _context.ResolveOwnedLegalEntityAsync(command.OwnerUserId, command.LegalEntityId, cancellationToken);

        if (resolution.ExplicitNotOwned)
        {
            return new GiftVouchersResult("CompanyNotOwned", ErrorMessage: "Company not found.");
        }

        if (resolution.LegalEntityId is not { } legalEntityId)
        {
            return new GiftVouchersResult("OwnerCompanyNotFound", ErrorMessage: "Owner does not have a legal entity profile.");
        }

        var isMember = await _context.CompanyMembers
            .AsNoTracking()
            .AnyAsync(x => x.LegalEntityId == legalEntityId && x.WorkerUserId == command.WorkerUserId, cancellationToken);

        if (!isMember)
        {
            return new GiftVouchersResult("WorkerNotMember", ErrorMessage: "Worker is not a member of this company.");
        }

        // Named so the notification can say which company issued the fuel. A worker may belong to
        // several (W1), so "you were given fuel" without a company would be genuinely ambiguous.
        var companyName = await _context.LegalEntities
            .AsNoTracking()
            .Where(e => e.Id == legalEntityId)
            .Select(e => e.Name)
            .FirstOrDefaultAsync(cancellationToken);

        var voucherIdSet = command.VoucherIds.Distinct().ToList();

        var vouchers = await _context.FuelVouchers
            .Where(x => voucherIdSet.Contains(x.Id))
            .ToListAsync(cancellationToken);

        if (vouchers.Count != voucherIdSet.Count)
        {
            return new GiftVouchersResult("VoucherNotFound", ErrorMessage: "One or more vouchers were not found.");
        }

        var invalidVoucher = vouchers.FirstOrDefault(v =>
            v.LegalEntityId != legalEntityId ||
            v.AssignedToUserId != command.OwnerUserId ||
            v.WorkerUserId != null ||
            v.Status != VoucherStatus.Assigned);

        if (invalidVoucher is not null)
        {
            return new GiftVouchersResult(
                "VoucherNotEligible",
                ErrorMessage: "All vouchers must be company-owned pool vouchers with Assigned status and no worker assignment.");
        }

        // Handing fuel to a worker is a handover, not a sale, so it gets an order of its own kind:
        // Price 0 (no money moved), LegalEntityId the company, SourceOrderId the purchase the fuel
        // came from. One order per gift action, so the worker sees one receipt per handover rather
        // than a flat list, and the batch the owner actually handed over is the batch that shows up.
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var now = SystemClock.UtcNow;

        // The vouchers may have been bought across several orders. One issuance order can only name
        // one parent, so SourceOrderId is set only when they all came from the same purchase.
        var purchaseOrderIds = vouchers
            .Select(v => v.OrderId)
            .Where(id => id is not null)
            .Distinct()
            .ToList();

        var sharedPurchaseOrderId = purchaseOrderIds.Count == 1 ? purchaseOrderIds[0] : null;

        // UnitPrice is the price the company itself paid, carried for information only. Price on the
        // order stays 0, and every money view excludes this kind, so this number is never mistaken
        // for revenue.
        var companyCostByFuel = await LoadCompanyCostAsync(sharedPurchaseOrderId, cancellationToken);

        var issuanceOrderId = Guid.CreateVersion7();

        var issuanceOrder = new Order
        {
            Id = issuanceOrderId,
            // The worker, not the owner: the order belongs to whoever now holds the fuel, which is
            // what makes it show up as a receipt in the worker's own wallet.
            UserId = command.WorkerUserId,
            LegalEntityId = legalEntityId,
            Price = 0,
            Kind = OrderKind.ReceivedFromCompany,
            SourceOrderId = sharedPurchaseOrderId,
            Status = OrderStatus.Fulfilled,
            FulfilledAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            LineItems = vouchers.Select(voucher =>
            {
                var key = (Brand: voucher.Brand.ToLowerInvariant(), voucher.FuelTypeId, voucher.Liters);
                var cost = companyCostByFuel.GetValueOrDefault(key);

                return new OrderLineItem
                {
                    Id = Guid.CreateVersion7(),
                    OrderId = issuanceOrderId,
                    Brand = voucher.Brand,
                    // The brand's key beside its name - the voucher's own key, because the line
                    // is about the same fuel the company hands over.
                    BrandId = voucher.BrandId,
                    FuelTypeId = voucher.FuelTypeId,
                    Liters = voucher.Liters,
                    Quantity = 1,
                    UnitPrice = cost,
                    LineTotal = cost
                };
            }).ToList()
        };

        _context.Orders.Add(issuanceOrder);

        // The lines are projected from the same `vouchers` list in the same order, so the mapping is
        // positional. Captured here rather than re-derived per voucher, because guessing it would put a
        // custody row on the wrong money line - and a wrong LineItemId is worse than none, since NULL
        // honestly says "this movement has no line" while a wrong id says something false.
        var lineIdByVoucher = vouchers
            .Select((voucher, index) => (VoucherId: voucher.Id, LineId: issuanceOrder.LineItems.ElementAt(index).Id))
            .ToDictionary(x => x.VoucherId, x => x.LineId);

        foreach (var voucher in vouchers)
        {
            voucher.WorkerUserId = command.WorkerUserId;
            // When the fuel changed hands. Its own column rather than UpdatedAtUtc, because a later
            // block or recall would move that one and the worker's report would claim a different
            // receipt date (#150).
            voucher.IssuedAtUtc = SystemClock.UtcNow;
            // The voucher now belongs to the handover, not to the purchase. It is still Assigned, and
            // ck_voucher_held_has_order forbids an orderless one.
            voucher.OrderId = issuanceOrder.Id;
            voucher.UpdatedAtUtc = now;

            _context.Update(voucher);

            // D-13. This is a change of place and the ledger's own guard proves it: the voucher was
            // `LegalEntityId == this company` and `WorkerUserId == null` a few lines ago, which is the
            // company holding stock. It is now in a worker's hands. `gift` is the last movement kind
            // with no writer, and this is the path that should have had one all along.
            //
            // No StatusChanged event, and deliberately: the voucher's status stays `Assigned` across a
            // gift. What changed is who holds it, not what it is — and an event claiming a transition
            // that did not happen would be exactly the "handler arranged it" row the exchange declines
            // to write.
            //
            // amount_uah stays NULL like every other movement with an order behind it: the money is
            // order_line_items.line_total, and this points at that line through order_id.
            _context.VoucherMovements.Add(new VoucherMovement
            {
                Id = Guid.CreateVersion7(),
                Kind = VoucherMovementKinds.Gift,
                VoucherId = voucher.Id,
                FromLocation = VoucherLocations.Company,
                ToLocation = VoucherLocations.Worker,
                OrderId = issuanceOrder.Id,
                LineItemId = lineIdByVoucher[voucher.Id],
                ActorKind = VoucherActorKinds.User,
                ActorUserId = command.OwnerUserId,
                ActorPartyId = legalEntityId,
                OccurredAtUtc = now
            });

            _context.Fulfillments.Add(new Fulfillment
            {
                OrderId = issuanceOrder.Id,
                VoucherId = voucher.Id,
                FulfilledAtUtc = now
            });
        }

        await _context.SaveChangesAsync(cancellationToken);

        // #150 W3 - tell the worker. Written into the outbox inside the same transaction as the
        // gift, so a committed issue always has its notification queued and a rolled-back one never
        // leaves a "you were given fuel" message pointing at fuel that was not actually handed over.
        _context.OutboxEvents.Add(new OutboxEvent
        {
            EventType = OutboxEventType.VoucherIssued,
            Payload = System.Text.Json.JsonSerializer.Serialize(new VoucherIssuedPayload
            {
                WorkerUserId = command.WorkerUserId.ToString(),
                CompanyName = companyName,
                VoucherCount = vouchers.Count,
                LegalEntityId = legalEntityId.ToString(),
            }),
            Processed = false,
            CreatedAtUtc = now,
        });

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _metrics.VouchersGifted(vouchers.Count);

        return new GiftVouchersResult("Success", vouchers.Count, IssuanceOrderId: issuanceOrder.Id);
    }

    /// <summary>Price per (brand, fuel, litres) line on the company's own purchase, for reference.</summary>
    private async Task<Dictionary<(string Brand, Guid FuelTypeId, decimal Liters), decimal>> LoadCompanyCostAsync(
        Guid? purchaseOrderId,
        CancellationToken cancellationToken)
    {
        if (purchaseOrderId is not { } orderId)
            return [];

        var lines = await _context.OrderLineItems
            .AsNoTracking()
            .Where(li => li.OrderId == orderId)
            .Select(li => new { li.Brand, li.FuelTypeId, li.Liters, li.UnitPrice })
            .ToListAsync(cancellationToken);

        return lines
            .GroupBy(li => (li.Brand.ToLowerInvariant(), li.FuelTypeId, li.Liters))
            .ToDictionary(g => g.Key, g => g.First().UnitPrice);
    }
}

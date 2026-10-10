using FuelFlow.API.Features.Orders.SharedServices;
using FuelFlow.Features.Company.SharedModels;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Observability;
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

        var now = DateTime.UtcNow;

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

        var issuanceOrderId = Guid.NewGuid();

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
                var key = (Provider: voucher.Provider.ToLowerInvariant(), voucher.FuelTypeId, voucher.Liters);
                var cost = companyCostByFuel.GetValueOrDefault(key);

                return new OrderLineItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = issuanceOrderId,
                    Provider = voucher.Provider,
                    FuelTypeId = voucher.FuelTypeId,
                    Liters = voucher.Liters,
                    Quantity = 1,
                    UnitPrice = cost,
                    LineTotal = cost
                };
            }).ToList()
        };

        _context.Orders.Add(issuanceOrder);

        foreach (var voucher in vouchers)
        {
            voucher.WorkerUserId = command.WorkerUserId;
            // When the fuel changed hands. Its own column rather than UpdatedAtUtc, because a later
            // block or recall would move that one and the worker's report would claim a different
            // receipt date (#150).
            voucher.IssuedAtUtc = DateTime.UtcNow;
            // The voucher now belongs to the handover, not to the purchase. It is still Assigned, and
            // ck_voucher_held_has_order forbids an orderless one.
            voucher.OrderId = issuanceOrder.Id;
            voucher.UpdatedAtUtc = now;

            _context.Update(voucher);

            _context.Fulfillments.Add(new Fulfillment
            {
                OrderId = issuanceOrder.Id,
                VoucherId = voucher.Id,
                FulfilledAtUtc = now
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _metrics.VouchersGifted(vouchers.Count);

        return new GiftVouchersResult("Success", vouchers.Count, IssuanceOrderId: issuanceOrder.Id);
    }

    /// <summary>Price per (provider, fuel, litres) line on the company's own purchase, for reference.</summary>
    private async Task<Dictionary<(string Provider, string FuelTypeId, decimal Liters), decimal>> LoadCompanyCostAsync(
        Guid? purchaseOrderId,
        CancellationToken cancellationToken)
    {
        if (purchaseOrderId is not { } orderId)
            return [];

        var lines = await _context.OrderLineItems
            .AsNoTracking()
            .Where(li => li.OrderId == orderId)
            .Select(li => new { li.Provider, li.FuelTypeId, li.Liters, li.UnitPrice })
            .ToListAsync(cancellationToken);

        return lines
            .GroupBy(li => (li.Provider.ToLowerInvariant(), li.FuelTypeId, li.Liters))
            .ToDictionary(g => g.Key, g => g.First().UnitPrice);
    }
}

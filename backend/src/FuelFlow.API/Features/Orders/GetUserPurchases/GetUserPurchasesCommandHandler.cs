using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Features.Vouchers.History;
using FuelFlow.Features.Vouchers.Renewal;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.SharedKernel.DTOs;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using VoucherDto = FuelFlow.SharedKernel.DTOs.VoucherDto;

namespace FuelFlow.Features.Orders.GetUserPurchases;

public sealed class GetUserPurchasesCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly IQrGenerator _qrGenerator;
    private readonly ILogger<GetUserPurchasesCommandHandler> _logger;

    public GetUserPurchasesCommandHandler(
        ApplicationDbContext context,
        IQrGenerator qrGenerator,
        ILogger<GetUserPurchasesCommandHandler> logger)
    {
        _context = context;
        _qrGenerator = qrGenerator;
        _logger = logger;
    }

    public async Task<List<PurchaseDto>> HandleAsync(
        GetUserPurchasesCommand command,
        CancellationToken cancellationToken = default)
    {
        var orders = await _context.Orders
            .AsNoTracking()
            .Include(o => o.LineItems)
            .Where(o => o.UserId == command.UserId)
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        if (orders.Count == 0)
            return [];

        var orderIds = orders.Select(o => o.Id).ToList();

        // Orders that are voucher renewals/replacements (have renewal items). The
        // mobile app labels these distinctly and keeps the renewed voucher in the
        // primary "available" list instead of nesting it under a purchase-looking order.
        var renewalOrderIds = (await _context.VoucherRenewalItems
            .AsNoTracking()
            .Where(i => orderIds.Contains(i.OrderId))
            .Select(i => i.OrderId)
            .Distinct()
            .ToListAsync(cancellationToken)).ToHashSet();

        var fulfillments = await _context.Fulfillments
            .AsNoTracking()
            .Where(f => orderIds.Contains(f.OrderId))
            .ToListAsync(cancellationToken);

        var voucherIds = fulfillments
            .Select(f => f.VoucherId)
            .Distinct()
            .ToList();

        // Every renewal line this user ever placed, flattened for the per-voucher history timeline.
        // Keyed off the user's own orders, so it already spans the full extend/replace lineage.
        var renewalNodes = (await _context.VoucherRenewalItems
            .AsNoTracking()
            .Where(i => orderIds.Contains(i.OrderId))
            .Select(i => new VoucherHistoryProjector.RenewalNode(
                i.SourceVoucherId,
                i.FulfilledVoucherId,
                i.FulfilledAtUtc ?? i.CreatedAtUtc,
                i.TermCode,
                i.AmountPaid,
                i.PreviousCustomerExpiration,
                i.NewCustomerExpiration))
            .ToListAsync(cancellationToken));

        var vouchers = voucherIds.Count != 0
            ? await _context.FuelVouchers
                .AsNoTracking()
                .Include(v => v.QrParameters)
                .Where(v => voucherIds.Contains(v.Id))
                .ToListAsync(cancellationToken)
            : [];

        var allFuelTypeIds = orders
            .SelectMany(o => o.LineItems.Select(li => li.FuelTypeId))
            .Concat(vouchers.Select(v => v.FuelTypeId))
            .Distinct()
            .ToList();

        var fuelTypeNames = await _context.FuelTypes
            .AsNoTracking()
            .Where(ft => allFuelTypeIds.Contains(ft.Id))
            .ToDictionaryAsync(ft => ft.Id, ft => ft.Name, cancellationToken);

        var fulfillmentsByOrder = fulfillments
            .GroupBy(f => f.OrderId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var vouchersById = vouchers.ToDictionary(v => v.Id);

        // Purchase lookup for the history projector: a voucher's owning purchase order and the slice
        // of its price attributable to that voucher. Renewal orders are excluded (a renewal is its own
        // event, not a purchase). The schema does not tie a voucher to a specific line item, so a
        // multi-voucher order's price is split evenly across the vouchers it delivered - exact for the
        // overwhelmingly common single-voucher order.
        var ordersById = orders.ToDictionary(o => o.Id);
        var purchaseVoucherCountByOrder = fulfillments
            .GroupBy(f => f.OrderId)
            .ToDictionary(g => g.Key, g => g.Select(f => f.VoucherId).Distinct().Count());

        VoucherHistoryProjector.PurchaseNode? PurchaseLookup(Guid voucherId)
        {
            var v = vouchersById.GetValueOrDefault(voucherId);
            if (v?.OrderId is not { } owningOrderId) return null;
            if (!ordersById.TryGetValue(owningOrderId, out var owningOrder)) return null;
            if (renewalOrderIds.Contains(owningOrderId)) return null;   // a renewal is not a purchase

            var count = purchaseVoucherCountByOrder.GetValueOrDefault(owningOrderId, 0);
            var amount = count > 0 ? owningOrder.Price / count : owningOrder.Price;
            return new VoucherHistoryProjector.PurchaseNode(
                owningOrder.FulfilledAtUtc ?? owningOrder.CreatedAtUtc,
                amount);
        }

        // The customer's ORIGINAL purchase order for a voucher, resolved across replace swaps so a
        // replacement is filed under the order the customer first bought - not the renewal order that
        // delivered the stock voucher. Falls back to the voucher's own owning order.
        Guid? ResolveOriginOrderId(Guid voucherId)
        {
            var rootId = VoucherHistoryProjector.ResolveRootVoucherId(voucherId, renewalNodes);
            var rootVoucher = vouchersById.GetValueOrDefault(rootId);
            var owningOrderId = rootVoucher?.OrderId
                ?? vouchersById.GetValueOrDefault(voucherId)?.OrderId;
            if (owningOrderId is { } id && !renewalOrderIds.Contains(id)) return id;
            return owningOrderId;
        }

        return orders.Select(order =>
        {
            var orderFulfillments = fulfillmentsByOrder.GetValueOrDefault(order.Id) ?? [];
            var orderVouchers = orderFulfillments
                .Select(f => vouchersById.GetValueOrDefault(f.VoucherId))
                .OfType<FuelVoucher>()
                .ToList();

            var lineItemsList = order.LineItems.ToList();
            var firstLi = lineItemsList.FirstOrDefault();
            var provider = firstLi?.Provider ?? "";
            var firstFuelTypeId = firstLi?.FuelTypeId ?? "";
            var fuelName = fuelTypeNames.GetValueOrDefault(firstFuelTypeId) ?? firstFuelTypeId;
            var totalLiters = lineItemsList.Sum(li => li.Liters * li.Quantity);
            var totalQuantity = lineItemsList.Sum(li => li.Quantity);

            return new PurchaseDto
            {
                Id = order.Id,
                ProductType = "",
                Provider = provider,
                FuelType = firstFuelTypeId,
                FuelName = fuelName,
                Liters = totalLiters,
                Quantity = totalQuantity,
                Price = order.Price,
                Status = order.Status.ToString(),
                MonobankInvoiceId = order.MonobankInvoiceId,
                MonobankPaymentUrl = order.MonobankPaymentUrl,
                MonobankStatus = order.MonobankStatus?.ToString(),
                LegalEntityId = order.LegalEntityId,
                CreatedAtUtc = order.CreatedAtUtc,
                FulfilledAtUtc = order.FulfilledAtUtc,
                IsRenewal = renewalOrderIds.Contains(order.Id),
                Kind = order.Kind.ToString(),
                LineItems = order.LineItems.Select(li => new OrderLineItemDto
                {
                    Id = li.Id,
                    FuelTypeId = li.FuelTypeId,
                    Liters = li.Liters,
                    Quantity = li.Quantity,
                    UnitPrice = li.UnitPrice,
                    LineTotal = li.LineTotal,
                    Provider = li.Provider,
                }).ToList(),
                Vouchers = orderVouchers.Select(v =>
                {
                    var imageUrl = GenerateQrImage(v);
                    return new VoucherDto
                    {
                        Id = v.Id,
                        Provider = v.Provider,
                        FuelType = v.FuelTypeId,
                        FuelName = fuelTypeNames.GetValueOrDefault(v.FuelTypeId) ?? v.FuelTypeId,
                        Liters = v.Liters,
                        Amount = v.Liters,
                        ExpirationDate = v.CustomerExpirationDate,
                        VoucherNumber = v.VoucherNumber,
                        ExternalId = v.VoucherNumber,
                        QrPayload = v.QrPayload,
                        QrCodeData = v.QrPayload,
                        Status = v.Status.ToString(),
                        LegalEntityId = v.LegalEntityId,
                        WorkerUserId = v.WorkerUserId,
                        ImageUrl = imageUrl,
                        History = VoucherHistoryProjector.Build(
                            v.Id, v.Liters, renewalNodes, PurchaseLookup),
                        OriginOrderId = ResolveOriginOrderId(v.Id),
                    };
                }).ToList()
            };
        }).ToList();
    }

    private string? GenerateQrImage(FuelVoucher voucher)
    {
        if (string.IsNullOrWhiteSpace(voucher.QrPayload))
        {
            _logger.LogWarning(
                "Voucher {VoucherId} ({VoucherNumber}) has no QR payload — cannot generate QR image",
                voucher.Id, voucher.VoucherNumber);
            return voucher.ImageUrl;
        }

        if (voucher.QrParameters is null)
        {
            _logger.LogWarning(
                "Voucher {VoucherId} ({VoucherNumber}) has no stored QR parameters — generated QR may differ from original",
                voucher.Id, voucher.VoucherNumber);
        }

        return "data:image/png;base64," + _qrGenerator.GenerateQrCode(
            voucher.QrPayload,
            eccLevel: voucher.QrParameters?.EccLevel,
            version: voucher.QrParameters?.Version,
            encodingMode: voucher.QrParameters?.EncodingMode,
            maskPattern: voucher.QrParameters?.MaskPattern);
    }
}

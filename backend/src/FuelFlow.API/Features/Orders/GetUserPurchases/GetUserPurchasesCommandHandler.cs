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

        // A renewal line is one that names the voucher it renews - the marker #1180 put on
        // order_line_items when the renewal's intent moved there, and the only definition there is
        // now that voucher_renewal_items is gone. The lines are already loaded by the Include above,
        // so this is a partition rather than another query.
        //
        // `Order.Kind == Renewal` is deliberately NOT used: an operator renewal is that kind too but
        // names no source voucher, and it is absent from this list today because it has no renewal
        // items. Keep it that way rather than silently promoting operator renewals into the wallet's
        // renewal view.
        var renewalLinesByOrder = orders.ToDictionary(
            o => o.Id,
            o => o.LineItems.Where(l => l.SourceVoucherId != null).ToList());

        // Orders that are voucher renewals/replacements. The mobile app labels these distinctly and
        // keeps the renewed voucher in the primary "available" list instead of nesting it under a
        // purchase-looking order.
        var renewalOrderIds = renewalLinesByOrder
            .Where(kv => kv.Value.Count > 0)
            .Select(kv => kv.Key)
            .ToHashSet();

        var fulfillments = await _context.Fulfillments
            .AsNoTracking()
            .Where(f => orderIds.Contains(f.OrderId))
            .ToListAsync(cancellationToken);

        var voucherIds = fulfillments
            .Select(f => f.VoucherId)
            .Distinct()
            .ToList();

        // Which voucher each renewal line actually delivered. IgnoreQueryFilters because the global
        // filter hides RELEASED deliveries, and a renewal whose voucher was later handed back is still
        // part of the timeline - it was an unfiltered column on voucher_renewal_items before this.
        var deliveredByLine = new Dictionary<Guid, Fulfillment>();
        foreach (var f in await _context.Fulfillments
                     .IgnoreQueryFilters()
                     .Where(f => f.LineItemId != null && orderIds.Contains(f.OrderId))
                     .OrderBy(f => f.FulfilledAtUtc)
                     .ToListAsync(cancellationToken))
        {
            // A renewal line delivers exactly one voucher. Ascending order means that if a line ever
            // did carry two, the later delivery wins - defensive, not expected.
            deliveredByLine[f.LineItemId!.Value] = f;
        }

        // Every renewal line this user ever placed, flattened for the per-voucher history timeline.
        // Keyed off the user's own orders, so it already spans the full extend/replace lineage.
        //
        // Two questions, two owners: the line says what was bought, for which voucher and for how
        // much (its LineTotal is the amount, since quantity is 1 for a renewal); the fulfilment says
        // which voucher the customer ended up holding and when. Undelivered lines still appear, dated
        // by the order, which is what the old CreatedAtUtc fallback did.
        var renewalNodes = orders
            .SelectMany(o => renewalLinesByOrder[o.Id].Select(l => new { Line = l, OrderCreatedAtUtc = o.CreatedAtUtc }))
            .Select(x =>
            {
                deliveredByLine.TryGetValue(x.Line.Id, out var delivered);
                return new VoucherHistoryProjector.RenewalNode(
                    x.Line.SourceVoucherId!.Value,
                    delivered?.VoucherId,
                    delivered?.FulfilledAtUtc ?? x.OrderCreatedAtUtc,
                    x.Line.TermCode,
                    x.Line.LineTotal,
                    x.Line.PreviousCustomerExpiration,
                    x.Line.NewCustomerExpiration);
            })
            .ToList();

        // Every voucher reachable through this user's orders — INCLUDING ones they have since given back. The
        // chain across a replace is only walkable while the released original is here: ResolveOriginOrderId
        // walks back to the ROOT voucher and reads ITS order_id to file the replacement under the purchase
        // the customer first made. Drop the original and the replacement resolves to the renewal order
        // instead, which the wallet does not render — the fuel disappears from the customer entirely.
        // So load the whole chain, and keep ownership as a separate concern further down.
        var chainVouchers = voucherIds.Count != 0
            ? await _context.FuelVouchers
                .AsNoTracking()
                .Include(v => v.QrParameters)
                .Where(v => voucherIds.Contains(v.Id))
                .ToListAsync(cancellationToken)
            : [];

        // What the customer is shown. A voucher they no longer hold is Fuel Flow's stock again, and once it
        // is Available the client has no status reason to hide it, so ownership has to be enforced here.
        var vouchers = chainVouchers
            .Where(v => v.AssignedToUserId == command.UserId || v.WorkerUserId == command.UserId)
            .ToList();

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

        // Keyed over the WHOLE chain, so origin/history resolution can see vouchers we do not return.
        var chainVouchersById = chainVouchers.ToDictionary(v => v.Id);
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
            // The root voucher's order is the purchase, and after a replace the ROOT may be a voucher we no
            // longer hold — so this must resolve over the whole chain, not only the returned vouchers.
            var v = chainVouchersById.GetValueOrDefault(voucherId);
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
                var rootVoucher = chainVouchersById.GetValueOrDefault(rootId);
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
            var brand = firstLi?.Brand ?? "";
            var firstFuelTypeId = firstLi?.FuelTypeId ?? Guid.Empty;
            var fuelName = fuelTypeNames.GetValueOrDefault(firstFuelTypeId) ?? firstFuelTypeId.ToString();
            var totalLiters = lineItemsList.Sum(li => li.Liters * li.Quantity);
            var totalQuantity = lineItemsList.Sum(li => li.Quantity);

            return new PurchaseDto
            {
                Id = order.Id,
                ProductType = "",
                Brand = brand,
                FuelType = firstFuelTypeId.ToString(),
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
                    Brand = li.Brand,
                }).ToList(),
                Vouchers = orderVouchers.Select(v =>
                {
                    var imageUrl = GenerateQrImage(v);
                    return new VoucherDto
                    {
                        Id = v.Id,
                        Brand = v.Brand,
                        FuelType = v.FuelTypeId.ToString(),
                        FuelName = fuelTypeNames.GetValueOrDefault(v.FuelTypeId) ?? v.FuelTypeId.ToString(),
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

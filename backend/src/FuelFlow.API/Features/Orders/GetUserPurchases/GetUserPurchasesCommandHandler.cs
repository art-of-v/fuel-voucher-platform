using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Import;
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
            .Include(o => o.LineItems)
            .Where(o => o.UserId == command.UserId)
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        if (orders.Count == 0)
            return [];

        var orderIds = orders.Select(o => o.Id).ToList();

        var fulfillments = await _context.Fulfillments
            .Where(f => orderIds.Contains(f.OrderId))
            .ToListAsync(cancellationToken);

        var voucherIds = fulfillments
            .Select(f => f.VoucherId)
            .Distinct()
            .ToList();

        var vouchers = voucherIds.Count != 0
            ? await _context.FuelVouchers
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
            .Where(ft => allFuelTypeIds.Contains(ft.Id))
            .ToDictionaryAsync(ft => ft.Id, ft => ft.Name, cancellationToken);

        var fulfillmentsByOrder = fulfillments
            .GroupBy(f => f.OrderId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var vouchersById = vouchers.ToDictionary(v => v.Id);

        return orders.Select(order =>
        {
            var orderFulfillments = fulfillmentsByOrder.GetValueOrDefault(order.Id) ?? [];
            var orderVouchers = orderFulfillments
                .Select(f => vouchersById.GetValueOrDefault(f.VoucherId))
                .Where(v => v != null)
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
                MonobankStatus = order.MonobankStatus?.ToString(),
                CreatedAtUtc = order.CreatedAtUtc,
                FulfilledAtUtc = order.FulfilledAtUtc,
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
                        ExpirationDate = v.ExpirationDate,
                        VoucherNumber = v.VoucherNumber,
                        ExternalId = v.VoucherNumber,
                        QrPayload = v.QrPayload,
                        QrCodeData = v.QrPayload,
                        Status = v.Status.ToString(),
                        ImageUrl = imageUrl
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

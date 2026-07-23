using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.SharedKernel.DTOs;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Orders.GetUserPurchases;

public sealed class GetUserPurchasesCommandHandler
{
    private readonly ApplicationDbContext _context;

    public GetUserPurchasesCommandHandler(ApplicationDbContext context)
    {
        _context = context;
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
                .Where(v => voucherIds.Contains(v.Id))
                .ToListAsync(cancellationToken)
            : [];

        var allFuelTypeIds = orders
            .Select(o => o.FuelTypeId)
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

            var fuelName = fuelTypeNames.GetValueOrDefault(order.FuelTypeId) ?? order.FuelTypeId;

            return new PurchaseDto
            {
                Id = order.Id,
                ProductType = order.ProductType,
                Provider = order.Provider,
                FuelType = order.FuelTypeId,
                FuelName = fuelName,
                Liters = order.Liters,
                Quantity = order.Quantity,
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
                    LineTotal = li.LineTotal
                }).ToList(),
                Vouchers = orderVouchers.Select(v => new VoucherDto
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
                    Status = v.Status.ToString()
                }).ToList()
            };
        }).ToList();
    }
}

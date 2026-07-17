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
            .Where(o => o.UserId == command.UserId)
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var purchaseDtos = new List<PurchaseDto>();

        foreach (var order in orders)
        {
            var fulfillments = await _context.Fulfillments
                .Where(f => f.OrderId == order.Id)
                .ToListAsync(cancellationToken);

            var voucherIds = fulfillments.Select(f => f.VoucherId).ToList();

            var vouchers = voucherIds.Any()
                ? await _context.FuelVouchers
                    .Where(v => voucherIds.Contains(v.Id))
                    .ToListAsync(cancellationToken)
                : new List<FuelFlow.Features.Vouchers.FuelVoucher>();

            purchaseDtos.Add(new PurchaseDto
            {
                Id = order.Id,
                ProductType = order.ProductType,
                Provider = order.Provider,
                FuelType = order.FuelTypeId,
                Liters = order.Liters,
                Quantity = order.Quantity,
                Price = order.Price,
                Status = order.Status.ToString(),
                MonobankInvoiceId = order.MonobankInvoiceId,
                MonobankStatus = order.MonobankStatus?.ToString(),
                CreatedAtUtc = order.CreatedAtUtc,
                FulfilledAtUtc = order.FulfilledAtUtc,
                Vouchers = vouchers.Select(v => new VoucherDto
                {
                    Id = v.Id,
                    Provider = v.Provider,
                    FuelType = v.FuelTypeId,
                    Liters = v.Liters,
                    Amount = v.Liters,
                    ExpirationDate = v.ExpirationDate,
                    VoucherNumber = v.VoucherNumber,
                    ExternalId = v.VoucherNumber,
                    QrPayload = v.QrPayload,
                    QrCodeData = v.QrPayload,
                    Status = v.Status.ToString()
                }).ToList()
            });
        }

        return purchaseDtos;
    }
}

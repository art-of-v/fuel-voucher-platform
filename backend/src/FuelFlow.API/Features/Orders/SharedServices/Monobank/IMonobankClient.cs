using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;

namespace FuelFlow.API.Features.Orders.SharedServices.Monobank;

public interface IMonobankClient
{
    Task<MonobankInvoiceResponse> CreateInvoiceAsync(MonobankInvoiceRequest request, CancellationToken cancellationToken = default);
    Task<MonobankInvoiceStatus> GetInvoiceStatusAsync(string invoiceId, CancellationToken cancellationToken = default);
}

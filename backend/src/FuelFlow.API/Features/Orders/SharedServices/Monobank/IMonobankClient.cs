using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;

namespace FuelFlow.API.Features.Orders.SharedServices.Monobank;

public interface IMonobankClient
{
    Task<MonobankInvoiceResponse> CreateInvoiceAsync(MonobankInvoiceRequest request, CancellationToken cancellationToken = default);
    Task<MonobankInvoiceStatus> GetInvoiceStatusAsync(string invoiceId, CancellationToken cancellationToken = default);
    Task<MonobankCancelResponse> CancelInvoiceAsync(string invoiceId, long amountKopecks, string extRef, CancellationToken cancellationToken = default);

    /// <summary>
    /// The acquirer's own record of what moved, for a period.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="GetInvoiceStatusAsync"/>: that asks about one invoice we already
    /// know about, this asks the counterparty what it believes happened across a whole period. It is
    /// the only source that can contradict us, which is the point - an invoice/status poll can only
    /// ever confirm what we already assumed.
    ///
    /// The range is half-open, <c>[from, to)</c>, so consecutive monthly periods tile without
    /// double-counting a row on the boundary.
    /// </remarks>
    /// <exception cref="HttpRequestException">The acquirer answered with an error status.</exception>
    Task<IReadOnlyList<MonobankStatementItem>> GetStatementAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);
}

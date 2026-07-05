namespace FuelFlow.Features.Vouchers.Import;

public interface IVoucherProviderParser
{
    bool CanParse(ProviderDetectionContext context);

    Task<IReadOnlyCollection<ParsedVoucher>> ParseAsync(
        ProviderParseContext context,
        CancellationToken cancellationToken);
}

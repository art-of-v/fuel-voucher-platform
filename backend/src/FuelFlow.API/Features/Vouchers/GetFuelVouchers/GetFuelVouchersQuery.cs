namespace FuelFlow.Features.Vouchers.GetFuelVouchers;

public sealed record GetFuelVouchersQuery(
    string? Search,
    string? Status,
    int Page,
    int PageSize);

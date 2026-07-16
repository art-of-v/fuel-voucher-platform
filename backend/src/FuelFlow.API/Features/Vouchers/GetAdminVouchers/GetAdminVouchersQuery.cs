namespace FuelFlow.Features.Vouchers.GetAdminVouchers;

public sealed record GetAdminVouchersQuery(
    int Page = 1,
    int Limit = 50,
    string? SortBy = null,
    string? SortDirection = null,
    string? FuelType = null,
    string? Status = null,
    string? Provider = null,
    string? Amount = null,
    string? ExpirationDate = null);

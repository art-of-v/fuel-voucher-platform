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
    string? ExpirationDate = null,
    Guid? WorkerUserId = null,
    /// <summary>
    /// Emulated test data: <c>only</c>, <c>exclude</c>, or unset for both. An emulated voucher's code
    /// does not exist at the station, so an operator looking at real stock needs to be able to take
    /// those rows out of the picture without deleting them mid-session.
    /// </summary>
    string? TestData = null);

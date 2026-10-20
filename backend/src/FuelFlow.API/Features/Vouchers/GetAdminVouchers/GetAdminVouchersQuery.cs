namespace FuelFlow.Features.Vouchers.GetAdminVouchers;

public sealed record GetAdminVouchersQuery(
    int Page = 1,
    int Limit = 50,
    string? SortBy = null,
    string? SortDirection = null,
    string? FuelType = null,
    /// <summary>
    /// Preferred over <see cref="FuelType"/>: fuel names are not unique across brands, so filtering
    /// by name can mix providers when a name happens to be shared. The admin sends this.
    /// </summary>
    Guid? FuelTypeId = null,
    string? Status = null,
    string? Brand = null,
    string? Amount = null,
    string? ExpirationDate = null,
    Guid? WorkerUserId = null,
    /// <summary>
    /// Emulated test data: <c>only</c>, <c>exclude</c>, or unset for both. An emulated voucher's code
    /// does not exist at the Brand, so an operator looking at real stock needs to be able to take
    /// those rows out of the picture without deleting them mid-session.
    /// </summary>
    string? TestData = null,
      /// <summary>
      /// Which supply route to show: <c>direct</c>, <c>partner</c>, or unset for both. Unset means both
      /// so an operator sees exactly what they saw before the column existed, on the same terms as
      /// <see cref="TestData"/> - and because this is a filter on how the stock was BOUGHT, not on who
      /// holds it or how it was paid for, a single route is expected to be the minority of any list.
      /// </summary>
      string? AcquisitionSource = null);

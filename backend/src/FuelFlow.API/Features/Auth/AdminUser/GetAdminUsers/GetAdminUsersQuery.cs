namespace FuelFlow.Features.Auth.AdminUser.GetAdminUsers;

public sealed record GetAdminUsersQuery(
    string? Role = null,
    bool? IsActive = null,
    string? Search = null,
    /// <summary>
    /// 1-based page number. Clamped by <see cref="PageLimits.ClampPage"/>.
    /// </summary>
    int Page = 1,
    /// <summary>
    /// Rows per page. Clamped by <see cref="PageLimits.ClampPageSize"/>, so one request cannot
    /// ask for every customer the platform has ever registered.
    /// </summary>
    int Limit = 50);

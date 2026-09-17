namespace FuelFlow.Features.Auth.AdminUser.GetAdminUsers;

public sealed record GetAdminUsersQuery(string? Role = null, bool? IsActive = null, string? Search = null);

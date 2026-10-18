namespace FuelFlow.Features.Brands.GetAdminBrandStations;

public sealed record GetAdminBrandStationsQuery(int Page = 1, int PageSize = 50, string? BrandId = null);

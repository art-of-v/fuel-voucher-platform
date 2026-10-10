using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.GetAdminVouchers;

public sealed class GetAdminVouchersQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetAdminVouchersQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<AdminVoucherListResponse> HandleAsync(
        GetAdminVouchersQuery query,
        CancellationToken cancellationToken = default)
    {
        var q = _context.FuelVouchers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(v => v.FuelType)
            .Include(v => v.WorkerUser)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Status) && Enum.TryParse<VoucherStatus>(query.Status, true, out var parsedStatus))
            q = q.Where(v => v.Status == parsedStatus);

        if (!string.IsNullOrWhiteSpace(query.Provider))
            q = q.Where(v => v.Provider == query.Provider);

        if (!string.IsNullOrWhiteSpace(query.FuelTypeId))
            q = q.Where(v => v.FuelTypeId == query.FuelTypeId);
        else if (!string.IsNullOrWhiteSpace(query.FuelType))
            q = q.Where(v => v.FuelType != null && v.FuelType.Name == query.FuelType);

        if (!string.IsNullOrWhiteSpace(query.Amount) && decimal.TryParse(query.Amount, out var parsedAmount))
            q = q.Where(v => v.Liters == parsedAmount);

        if (!string.IsNullOrWhiteSpace(query.ExpirationDate) && DateOnly.TryParse(query.ExpirationDate, out var parsedDate))
            q = q.Where(v => v.ProviderExpirationDate == parsedDate);

        if (query.WorkerUserId.HasValue)
            q = q.Where(v => v.WorkerUserId == query.WorkerUserId.Value);

        // Unset means both, so an operator sees exactly what they saw before the flag existed.
        q = query.TestData?.Trim().ToLowerInvariant() switch
        {
            "only" => q.Where(v => v.IsTestData),
            "exclude" => q.Where(v => !v.IsTestData),
            _ => q
        };

        var total = await q.CountAsync(cancellationToken);

        var ordered = (query.SortBy, query.SortDirection) switch
        {
            ("createdAt", "asc") => q.OrderBy(v => v.CreatedAtUtc),
            ("expirationDate", "asc") => q.OrderBy(v => v.ProviderExpirationDate),
            ("amount", "asc") => q.OrderBy(v => v.Liters),
            ("provider", "asc") => q.OrderBy(v => v.Provider),
            ("fuelType", "asc") => q.OrderBy(v => v.FuelType != null ? v.FuelType.Name : ""),
            ("status", "asc") => q.OrderBy(v => v.Status),
            ("createdAt", _) => q.OrderByDescending(v => v.CreatedAtUtc),
            ("expirationDate", _) => q.OrderByDescending(v => v.ProviderExpirationDate),
            ("amount", _) => q.OrderByDescending(v => v.Liters),
            ("provider", _) => q.OrderByDescending(v => v.Provider),
            ("fuelType", _) => q.OrderByDescending(v => v.FuelType != null ? v.FuelType.Name : ""),
            ("status", _) => q.OrderByDescending(v => v.Status),
            _ => q.OrderByDescending(v => v.CreatedAtUtc)
        };

        // Clamp server-side: Limit arrives from the query string, and this projection
        // covers the whole voucher inventory. See PageLimits.
        var page = PageLimits.ClampPage(query.Page);
        var limit = PageLimits.ClampPageSize(query.Limit);

        var items = await ordered
            .Skip(PageLimits.SkipFor(page, limit))
            .Take(limit)
            .ToListAsync(cancellationToken);

        var data = items.Select(v => new AdminVoucherListItemDto
        {
            Id = v.Id,
            QrPayload = v.QrPayload,
            Liters = v.Liters,
            FuelTypeId = v.FuelTypeId,
            FuelType = v.FuelType == null ? null : new FuelTypeRefDto { Id = v.FuelType.Id, Name = v.FuelType.Name },
            Provider = v.Provider,
            ProviderExpirationDate = v.ProviderExpirationDate,
            CustomerExpirationDate = v.CustomerExpirationDate,
            VoucherNumber = v.VoucherNumber,
            Status = v.Status.ToString(),
            WorkerUserId = v.WorkerUserId,
            WorkerFirstName = v.WorkerUser?.FirstName,
            WorkerLastName = v.WorkerUser?.LastName,
            CreatedAtUtc = v.CreatedAtUtc,
            ImageUrl = v.ImageUrl,
            IsTestData = v.IsTestData
        }).ToList();

        var globalTotal = await _context.FuelVouchers.IgnoreQueryFilters().CountAsync(cancellationToken);
        var testDataTotal = await _context.FuelVouchers.CountAsync(v => v.IsTestData, cancellationToken);
        var fuelTypeOptions = await _context.FuelVouchers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(v => v.FuelType != null)
            .Select(v => new { v.Provider, Id = v.FuelType!.Id, Name = v.FuelType!.Name })
            .Distinct()
            .ToListAsync(cancellationToken);
        var providers = await _context.FuelVouchers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(v => v.Provider)
            .Distinct()
            .ToListAsync(cancellationToken);
        var amounts = await _context.FuelVouchers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(v => v.Liters)
            .Distinct()
            .OrderBy(a => a)
            .ToListAsync(cancellationToken);

        return new AdminVoucherListResponse
        {
            Data = data,
            Total = total,
            GlobalTotal = globalTotal,
            TestDataTotal = testDataTotal,
            FuelTypes = fuelTypeOptions
                .Select(o => o.Name)
                .Distinct()
                .OrderBy(n => n, StringComparer.CurrentCulture)
                .ToList(),
            FuelTypesByProvider = fuelTypeOptions
                .GroupBy(o => o.Provider, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => g
                        .Select(o => new FuelTypeRefDto { Id = o.Id, Name = o.Name })
                        .OrderBy(o => o.Name, StringComparer.CurrentCulture)
                        .ToList(),
                    StringComparer.Ordinal),
            Providers = providers,
            Amounts = amounts,
            Statuses = ["Imported", "Available", "Assigned", "Used", "Expired", "Blocked"]
        };
    }
}

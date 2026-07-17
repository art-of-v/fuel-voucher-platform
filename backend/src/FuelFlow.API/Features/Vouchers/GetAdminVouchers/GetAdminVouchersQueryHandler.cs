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
            .AsNoTracking()
            .Include(v => v.FuelType)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Status) && Enum.TryParse<VoucherStatus>(query.Status, true, out var parsedStatus))
            q = q.Where(v => v.Status == parsedStatus);

        if (!string.IsNullOrWhiteSpace(query.Provider))
            q = q.Where(v => v.Provider == query.Provider);

        if (!string.IsNullOrWhiteSpace(query.FuelType))
            q = q.Where(v => v.FuelType != null && v.FuelType.Name == query.FuelType);

        if (!string.IsNullOrWhiteSpace(query.Amount) && decimal.TryParse(query.Amount, out var parsedAmount))
            q = q.Where(v => v.Liters == parsedAmount);

        if (!string.IsNullOrWhiteSpace(query.ExpirationDate) && DateOnly.TryParse(query.ExpirationDate, out var parsedDate))
            q = q.Where(v => v.ExpirationDate == parsedDate);

        var total = await q.CountAsync(cancellationToken);

        var ordered = (query.SortBy, query.SortDirection) switch
        {
            ("createdAt", "asc") => q.OrderBy(v => v.CreatedAtUtc),
            ("expirationDate", "asc") => q.OrderBy(v => v.ExpirationDate),
            ("amount", "asc") => q.OrderBy(v => v.Liters),
            ("provider", "asc") => q.OrderBy(v => v.Provider),
            ("fuelType", "asc") => q.OrderBy(v => v.FuelType != null ? v.FuelType.Name : ""),
            ("status", "asc") => q.OrderBy(v => v.Status),
            ("createdAt", _) => q.OrderByDescending(v => v.CreatedAtUtc),
            ("expirationDate", _) => q.OrderByDescending(v => v.ExpirationDate),
            ("amount", _) => q.OrderByDescending(v => v.Liters),
            ("provider", _) => q.OrderByDescending(v => v.Provider),
            ("fuelType", _) => q.OrderByDescending(v => v.FuelType != null ? v.FuelType.Name : ""),
            ("status", _) => q.OrderByDescending(v => v.Status),
            _ => q.OrderByDescending(v => v.CreatedAtUtc)
        };

        var items = await ordered
            .Skip((query.Page - 1) * query.Limit)
            .Take(query.Limit)
            .ToListAsync(cancellationToken);

        var data = items.Select(v => new AdminVoucherListItemDto
        {
            Id = v.Id,
            QrPayload = v.QrPayload,
            Liters = v.Liters,
            FuelTypeId = v.FuelTypeId,
            FuelType = v.FuelType == null ? null : new FuelTypeRefDto { Id = v.FuelType.Id, Name = v.FuelType.Name },
            Provider = v.Provider,
            ExpirationDate = v.ExpirationDate,
            VoucherNumber = v.VoucherNumber,
            Status = v.Status.ToString(),
            CreatedAtUtc = v.CreatedAtUtc,
            ImageUrl = v.ImageUrl
        }).ToList();

        var globalTotalTask = _context.FuelVouchers.CountAsync(cancellationToken);
        var fuelTypesTask = _context.FuelTypes
            .AsNoTracking()
            .Select(ft => ft.Name)
            .Distinct()
            .ToListAsync(cancellationToken);
        var providersTask = _context.FuelVouchers
            .AsNoTracking()
            .Select(v => v.Provider)
            .Distinct()
            .ToListAsync(cancellationToken);
        var amountsTask = _context.FuelVouchers
            .AsNoTracking()
            .Select(v => v.Liters)
            .Distinct()
            .OrderBy(a => a)
            .ToListAsync(cancellationToken);

        await Task.WhenAll(globalTotalTask, fuelTypesTask, providersTask, amountsTask);

        return new AdminVoucherListResponse
        {
            Data = data,
            Total = total,
            GlobalTotal = globalTotalTask.Result,
            FuelTypes = fuelTypesTask.Result,
            Providers = providersTask.Result,
            Amounts = amountsTask.Result,
            Statuses = ["Imported", "Available", "Assigned", "Used", "Expired"]
        };
    }
}

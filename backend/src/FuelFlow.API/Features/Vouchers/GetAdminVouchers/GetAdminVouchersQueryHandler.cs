using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.Domain;
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
            .IncludingSoftDeleted()
            .AsNoTracking()
            .Include(v => v.FuelType)
            .Include(v => v.WorkerUser)
            .Include(v => v.Supplier)
            .AsQueryable();

        // The global total is the whole table, and the filtered total is the same number when nothing
        // is filtered - so it is counted once, not twice.
        //
        // The flag is set in the same branch that narrows the list, so it cannot drift from the
        // logic: a filter that parses to nothing ("Amount=abc") narrows nothing, and one that names
        // a source we do not know narrows nothing either.
        var hasFilter = false;

        if (!string.IsNullOrWhiteSpace(query.Status) && Enum.TryParse<VoucherStatus>(query.Status, true, out var parsedStatus))
        {
            q = q.Where(v => v.Status == parsedStatus);
            hasFilter = true;
        }

        if (!string.IsNullOrWhiteSpace(query.Brand))
        {
            q = q.Where(v => v.Brand == query.Brand);
            hasFilter = true;
        }

        if (query.FuelTypeId.HasValue)
        {
            q = q.Where(v => v.FuelTypeId == query.FuelTypeId.Value);
            hasFilter = true;
        }
        else if (!string.IsNullOrWhiteSpace(query.FuelType))
        {
            q = q.Where(v => v.FuelType != null && v.FuelType.Name == query.FuelType);
            hasFilter = true;
        }

        if (!string.IsNullOrWhiteSpace(query.Amount) && decimal.TryParse(query.Amount, out var parsedAmount))
        {
            q = q.Where(v => v.Liters == parsedAmount);
            hasFilter = true;
        }

        if (!string.IsNullOrWhiteSpace(query.ExpirationDate) && DateOnly.TryParse(query.ExpirationDate, out var parsedDate))
        {
            q = q.Where(v => v.SupplierExpirationDate == parsedDate);
            hasFilter = true;
        }

        if (query.WorkerUserId.HasValue)
        {
            q = q.Where(v => v.WorkerUserId == query.WorkerUserId.Value);
            hasFilter = true;
        }

        // Unset means both, so an operator sees exactly what they saw before the flag existed. Both arms
        // compare the mapped column, not the IsTestData shim: a [NotMapped] getter is not
        // translatable, so the boolean form of this filter threw at run time.
        q = query.TestData?.Trim().ToLowerInvariant() switch
        {
            "only" => q.Where(v => v.DataKind == DataKinds.Qa),
            "exclude" => q.Where(v => v.DataKind == DataKinds.Real),
            _ => q
        };
        if (query.TestData?.Trim().ToLowerInvariant() is "only" or "exclude")
        {
            hasFilter = true;
        }

        // Same "unset means both" contract as TestData above, and for a sharper reason: the column
        // existed for a while before any voucher was acquired on the secondary route, so a filter
        // that defaulted to one arm would have hidden the entire inventory until someone noticed.
        // Compared against the mapped column rather than a NotMapped shim, for the same reason the
        // TestData arms are: a computed getter is not translatable.
        if (!string.IsNullOrWhiteSpace(query.AcquisitionSource))
        {
            var requested = query.AcquisitionSource.Trim().ToLowerInvariant();
            if (AcquisitionSources.IsKnown(requested))
            {
                q = q.Where(v => v.AcquisitionSource == requested);
                hasFilter = true;
            }
        }

        // The global total is the whole table. The filtered total is the same number when nothing
        // is filtered, and counting the table twice was a third of what this screen costs.
        //
        // The two counts stay on the table, and only the three facets moved to the summary. The
        // counts are 13.5 ms and 1.5 ms warm on the production-shaped replica (20.9 ms and 1.8 ms
        // cold) - a seventh of what the three facets cost - and a count OF the table cannot be out
        // of step with the table. The facets were 164 ms warm and 430 ms cold, which is why they
        // moved.
        var globalTotal = await _context.FuelVouchers.IncludingSoftDeleted().CountAsync(cancellationToken);
        var total = hasFilter
            ? await q.CountAsync(cancellationToken)
            : globalTotal;

        // Every arm ends in the same tie-break on Id. None of these sort keys is unique - sorting by
        // supplier puts every voucher from one supplier next to its neighbour, and they routinely
        // share an amount, a fuel and a status - so the key alone leaves the order of a whole page
        // up to the planner. Paging over that shows a row on two pages, or on neither, and the
        // operator is counting a stock that does not add up. The tie-break costs one comparison and
        // makes the page boundary real.
        //
        // The two cost/supplier arms also lead with a descending boolean, which is what puts the
        // un-priced rows last. Postgres sorts NULLs first on a descending sort, so "cheapest first"
        // read literally would put every voucher whose cost was never entered at the top of the
        // list. A missing cost is not the cheapest cost - it is an unknown one, and it should not
        // outrank what was actually recorded.
        var ordered = (query.SortBy, query.SortDirection) switch
        {
            ("createdAt", "asc") => q.OrderBy(v => v.CreatedAtUtc).ThenBy(v => v.Id),
            ("expirationDate", "asc") => q.OrderBy(v => v.SupplierExpirationDate).ThenBy(v => v.Id),
            ("amount", "asc") => q.OrderBy(v => v.Liters).ThenBy(v => v.Id),
            ("brand", "asc") => q.OrderBy(v => v.Brand).ThenBy(v => v.Id),
            ("fuelType", "asc") => q.OrderBy(v => v.FuelType != null ? v.FuelType.Name : "").ThenBy(v => v.Id),
            ("status", "asc") => q.OrderBy(v => v.Status).ThenBy(v => v.Id),
            // The admin labels this column "external ID" but renders it from the voucher number, so
            // the number is what it has to sort by. It was offered as sortable and fell through to
            // the default order: the header drew an arrow and the list ignored it.
            ("externalId", "asc") => q.OrderBy(v => v.VoucherNumber).ThenBy(v => v.Id),
            ("supplier", "asc") => q.OrderByDescending(v => v.Supplier != null)
                .ThenBy(v => v.Supplier != null ? v.Supplier.Name : "").ThenBy(v => v.Id),
            ("supplier", _) => q.OrderByDescending(v => v.Supplier != null)
                .ThenByDescending(v => v.Supplier != null ? v.Supplier.Name : "").ThenBy(v => v.Id),
            ("costPerLiter", "asc") => q.OrderByDescending(v => v.CostPerLiter != null)
                .ThenBy(v => v.CostPerLiter).ThenBy(v => v.Id),
            ("costPerLiter", _) => q.OrderByDescending(v => v.CostPerLiter != null)
                .ThenByDescending(v => v.CostPerLiter).ThenBy(v => v.Id),
            ("createdAt", _) => q.OrderByDescending(v => v.CreatedAtUtc).ThenBy(v => v.Id),
            ("expirationDate", _) => q.OrderByDescending(v => v.SupplierExpirationDate).ThenBy(v => v.Id),
            ("amount", _) => q.OrderByDescending(v => v.Liters).ThenBy(v => v.Id),
            ("brand", _) => q.OrderByDescending(v => v.Brand).ThenBy(v => v.Id),
            ("fuelType", _) => q.OrderByDescending(v => v.FuelType != null ? v.FuelType.Name : "").ThenBy(v => v.Id),
            ("status", _) => q.OrderByDescending(v => v.Status).ThenBy(v => v.Id),
            ("externalId", _) => q.OrderByDescending(v => v.VoucherNumber).ThenBy(v => v.Id),
            _ => q.OrderByDescending(v => v.CreatedAtUtc).ThenBy(v => v.Id)
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
            // No QrPayload here, and that is the point: the list used to carry the redeemable
            // payload of every row, so ONE staff call handed out every live voucher's code - the
            // same code the customer redeems at the pump, to whoever asked. The operator needs the
            // number, the status and the expiry; the code is for the one voucher they are about
            // to act on, and it has its own capability (AuthPolicies.VoucherPayload).
            Liters = v.Liters,
            FuelTypeId = v.FuelTypeId,
            FuelType = v.FuelType == null ? null : new FuelTypeRefDto { Id = v.FuelType.Id.ToString(), Name = v.FuelType.Name },
            Brand = v.Brand,
            SupplierId = v.SupplierId,
            SupplierName = v.Supplier?.Name,
            CostPerLiter = v.CostPerLiter,
            SupplierExpirationDate = v.SupplierExpirationDate,
            CustomerExpirationDate = v.CustomerExpirationDate,
            VoucherNumber = v.VoucherNumber,
            Status = v.Status.ToString(),
            WorkerUserId = v.WorkerUserId,
            WorkerFirstName = v.WorkerUser?.FirstName,
            WorkerLastName = v.WorkerUser?.LastName,
            CreatedAtUtc = v.CreatedAtUtc,
            IsTestData = v.DataKind == DataKinds.Qa,
            AcquisitionSource = v.AcquisitionSource,
            IsDeleted = v.IsDeleted
        }).ToList();

        var testDataTotal = await _context.FuelVouchers.CountAsync(v => v.DataKind == DataKinds.Qa, cancellationToken);

        // The three facets, read from the summary the database keeps of the inventory.
        //
        // They used to be three DISTINCT queries over the voucher table itself, which is the
        // largest table in the schema: on the production-shaped replica (500 000 vouchers) they
        // cost 68 ms, 45 ms and 51 ms warm - 430 ms of the ~455 ms the whole cold request took -
        // to produce three, four and twenty-three answers.
        //
        // They cannot come from the catalogue either: the catalogue is a SUPERSET of what the
        // vouchers carry, so a dropdown built from it offers a combination no voucher has, the
        // operator picks it, gets an empty page, and reads it as lost data. That is what
        // `AdminVouchers_ListsFuelTypesPerProvider_SoTheOptionsCannotMix` pins.
        //
        // The summary is the middle: the inventory itself, counted once by a trigger on
        // fuel_vouchers, so these three read a few dozen rows instead of half a million.
        //
        // The scope is the SAME as the list's, deliberately: the list counts with
        // With the soft-delete scope, the soft-deleted rows are in the list, and a dropdown that hid a fuel
        // the list can still show would be a filter the operator could not undo.
        var fuelTypeOptions = await (
            from summary in _context.StockSummary.AsNoTracking()
            join fuel in _context.FuelTypes.AsNoTracking() on summary.FuelTypeId equals fuel.Id
            where summary.VoucherCount > 0
            select new { summary.Brand, Id = fuel.Id, Name = fuel.Name })
            .Distinct()
            .ToListAsync(cancellationToken);

        var providers = await _context.StockSummary
            .AsNoTracking()
            .Where(s => s.VoucherCount > 0)
            .Select(s => s.Brand)
            .Distinct()
            .ToListAsync(cancellationToken);

        var amounts = await _context.StockSummary
            .AsNoTracking()
            .Where(s => s.VoucherCount > 0)
            .Select(s => s.Liters)
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
                .GroupBy(o => o.Brand, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => g
                        .Select(o => new FuelTypeRefDto { Id = o.Id.ToString(), Name = o.Name })
                        .OrderBy(o => o.Name, StringComparer.CurrentCulture)
                        .ToList(),
                    StringComparer.Ordinal),
            Providers = providers,
            Amounts = amounts,
            Statuses = ["Imported", "Available", "Assigned", "Used", "Expired", "Blocked"]
        };
    }
}

namespace FuelFlow.Features.Vouchers.GetAdminVouchers;

public sealed class AdminVoucherListItemDto
{
    public Guid Id { get; set; }
    public string? QrPayload { get; set; }
    public decimal Liters { get; set; }
    public Guid FuelTypeId { get; set; }
    public FuelTypeRefDto? FuelType { get; set; }
    public string Brand { get; set; } = null!;

    /// <summary>
    /// The company this voucher was bought from. Distinct from <see cref="Brand"/>, which is the
    /// brand printed on it (OKKO, Shell) - a brand says what the fuel is, a supplier says who we owe.
    /// Null until a voucher is imported against a supplier.
    /// </summary>
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }

    /// <summary>
    /// What this one voucher cost us, UAH per liter, stamped by its import batch. Null until a cost is
    /// entered. Null is worth showing rather than hiding: a blank cell here means "we do not know what
    /// we paid", which is a different problem from a zero.
    /// </summary>
    public decimal? CostPerLiter { get; set; }

    /// <summary>Real supplier term, from the printed document. Never moves after import.</summary>
    public DateOnly SupplierExpirationDate { get; set; }

    /// <summary>What the customer was sold; differs from the supplier term only once we sell short terms.</summary>
    public DateOnly CustomerExpirationDate { get; set; }

    public string VoucherNumber { get; set; } = null!;
    public string Status { get; set; } = null!;
    public Guid? WorkerUserId { get; set; }
    public string? WorkerFirstName { get; set; }
    public string? WorkerLastName { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    /// <summary>
    /// Emulated test data rather than fuel the operator paid a supplier for. The code does not exist
    /// at the Brand, so this row must never reach a paying customer.
    /// </summary>
    public bool IsTestData { get; set; }

    /// <summary>
    /// Which supply route this voucher was acquired on - <c>direct</c> or <c>partner</c>. Immutable
    /// and stated at acquisition; the cost of a voucher is a function of it, which is why an operator
    /// needs to be able to filter stock by route rather than discover it from a total.
    /// </summary>
    public string AcquisitionSource { get; set; } = null!;

    /// <summary>
    /// Archived by the soft delete (#217). The query behind this list calls
    /// <c>IgnoreQueryFilters</c> so an archived voucher is still listed - that is the only way an
    /// operator can find it again to un-archive it - which meant archived rows were being rendered
    /// next to live ones with nothing to tell them apart. The flag has to travel with the row.
    /// </summary>
    public bool IsDeleted { get; set; }
}

public sealed class FuelTypeRefDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
}

public sealed class AdminVoucherListResponse
{
    public List<AdminVoucherListItemDto> Data { get; set; } = [];
    public int Total { get; set; }
    public int GlobalTotal { get; set; }

    /// <summary>How many emulated vouchers exist, whatever the filters say - so the operator is never misled
    /// by an empty list that actually hides them.</summary>
    public int TestDataTotal { get; set; }
    public List<string> FuelTypes { get; set; } = [];
    /// <summary>
    /// Which fuels each brand actually has vouchers for, keyed by <c>fuel_vouchers.brand</c>.
    /// Without this the fuel-type dropdown offered every fuel in the catalog no matter which brand
    /// was selected, so the operator could pick a pairing that can never match a row and be shown an
    /// empty list. Built from the vouchers themselves rather than the catalog, so an option is only
    /// offered when it can return something.
    /// </summary>
    public Dictionary<string, List<FuelTypeRefDto>> FuelTypesByProvider { get; set; } = [];
    public List<string> Providers { get; set; } = [];
    public List<string> Statuses { get; set; } = [];
    public List<decimal> Amounts { get; set; } = [];
}

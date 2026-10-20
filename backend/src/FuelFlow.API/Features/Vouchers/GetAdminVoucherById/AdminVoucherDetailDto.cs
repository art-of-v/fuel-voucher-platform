namespace FuelFlow.Features.Vouchers.GetAdminVoucherById;

public sealed class AdminVoucherDetailDto
{
    public Guid Id { get; set; }
    public string? QrPayload { get; set; }
    public decimal Liters { get; set; }
    public Guid FuelTypeId { get; set; }
    public FuelTypeRefDto? FuelType { get; set; }
    public string Brand { get; set; } = null!;

    /// <summary>
    /// The same two facts the list row carries (#227): who we bought this voucher from, and what
    /// this one cost. The list and the single-voucher view answer the same question, and a detail
    /// screen that dropped them would tell an operator to go back to the row to learn what the row
    /// already had.
    /// </summary>
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }

    /// <summary>UAH per liter. Null is "we never recorded what we paid", which is not a cost of zero.</summary>
    public decimal? CostPerLiter { get; set; }

    /// <summary>Real supplier term, from the printed document. Never moves after import.</summary>
    public DateOnly SupplierExpirationDate { get; set; }

    /// <summary>What the customer was sold; differs from the supplier term only once we sell short terms.</summary>
    public DateOnly CustomerExpirationDate { get; set; }

    public string VoucherNumber { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    public string? ImageUrl { get; set; }
    public string? QrImage { get; set; }

    /// <summary>Emulated test data, not fuel paid for at the supplier. Never sellable to a real customer.</summary>
    public bool IsTestData { get; set; }
}

public sealed class FuelTypeRefDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
}
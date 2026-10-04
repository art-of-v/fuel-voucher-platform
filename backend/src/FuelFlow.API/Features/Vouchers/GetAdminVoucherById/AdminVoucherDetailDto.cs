namespace FuelFlow.Features.Vouchers.GetAdminVoucherById;

public sealed class AdminVoucherDetailDto
{
    public Guid Id { get; set; }
    public string? QrPayload { get; set; }
    public decimal Liters { get; set; }
    public string FuelTypeId { get; set; } = null!;
    public FuelTypeRefDto? FuelType { get; set; }
    public string Provider { get; set; } = null!;

    /// <summary>Real supplier term, from the printed document. Never moves after import.</summary>
    public DateOnly ProviderExpirationDate { get; set; }

    /// <summary>What the customer was sold; differs from the provider term only once we sell short terms.</summary>
    public DateOnly CustomerExpirationDate { get; set; }

    public string VoucherNumber { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    public string? ImageUrl { get; set; }
    public string? QrImage { get; set; }
}

public sealed class FuelTypeRefDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
}

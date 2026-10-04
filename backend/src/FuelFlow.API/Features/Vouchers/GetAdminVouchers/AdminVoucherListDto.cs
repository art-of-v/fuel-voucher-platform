namespace FuelFlow.Features.Vouchers.GetAdminVouchers;

public sealed class AdminVoucherListItemDto
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
    public Guid? WorkerUserId { get; set; }
    public string? WorkerFirstName { get; set; }
    public string? WorkerLastName { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? ImageUrl { get; set; }
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
    public List<string> FuelTypes { get; set; } = [];
    public List<string> Providers { get; set; } = [];
    public List<string> Statuses { get; set; } = [];
    public List<decimal> Amounts { get; set; } = [];
}

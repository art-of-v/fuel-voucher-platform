namespace FuelFlow.Features.Vouchers;

public class VoucherImportError
{
    public Guid Id { get; set; }
    public Guid ImportId { get; set; }
    public int PageNumber { get; set; }
    public string? VoucherNumber { get; set; }
    public string ErrorMessage { get; set; } = null!;
    public string? RawText { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

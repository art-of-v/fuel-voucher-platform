using FuelFlow.SharedKernel.Domain;
using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Features.Vouchers.SharedModels;

namespace FuelFlow.Features.Vouchers;

public class FuelVoucher
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = null!;
    public string FuelTypeId { get; set; } = null!;
    public FuelTypeEntity? FuelType { get; set; }
    public decimal Liters { get; set; }
    public DateOnly ExpirationDate { get; set; }
    public string VoucherNumber { get; set; } = null!;
    public string QrPayload { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }

    public VoucherStatus Status { get; set; }
    public string? FuelSubtype { get; set; }
    public string? RedemptionRules { get; set; }
    public string? ImageUrl { get; set; }
    public string? AssignedToUserId { get; set; }
    public Guid? ImportJobId { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public Guid? QrParametersId { get; set; }
    public QrParameters? QrParameters { get; set; }

    public double? VerificationMismatchPercent { get; set; }
    public int? VerificationMismatchedModules { get; set; }
    public int? VerificationTotalModules { get; set; }
}

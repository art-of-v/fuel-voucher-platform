using FuelFlow.Features.Contracts.SharedModels;
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
    public string? ExternalId { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public VoucherStatus Status { get; set; }
    public string? FuelSubtype { get; set; }
    public string? RedemptionRules { get; set; }
    public string? ImageUrl { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public Guid? LegalEntityId { get; set; }
    public Guid? WorkerUserId { get; set; }
    public Guid? ImportJobId { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public Guid? QrParametersId { get; set; }
    public QrParameters? QrParameters { get; set; }

    public User? AssignedToUser { get; set; }
    public LegalEntity? LegalEntity { get; set; }
    public User? WorkerUser { get; set; }
    public VoucherImport? ImportJob { get; set; }

    public bool IsDeleted { get; set; }

    public double? VerificationMismatchPercent { get; set; }
    public int? VerificationMismatchedModules { get; set; }
    public int? VerificationTotalModules { get; set; }
}

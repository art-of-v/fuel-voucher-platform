namespace FuelFlow.Features.Vouchers.SharedModels;

public enum VoucherStatus
{
    Imported,
    VerifiedWithWarnings,
    VerificationFailed,
    Available,
    Assigned,
    Used,
    Expired
}

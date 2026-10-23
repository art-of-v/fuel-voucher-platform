namespace FuelFlow.Features.Vouchers.SharedModels;

public enum VoucherStatus
{
    /// <summary>Read and stored, not yet offered for sale. The QR check passed or was not possible.</summary>
    Imported,

    /// <summary>
    /// The printed QR matrix disagreed with the decoded payload by 1-2% of modules. This is an import-QA
    /// warning about the image, not a finding about the supplier.
    /// </summary>
    DecodeIntegrityWarning,

    /// <summary>
    /// The printed QR matrix disagreed with the decoded payload by more than 2% of modules, so the voucher
    /// is kept out of stock. This says the image and the payload diverged; it does not say the voucher is
    /// counterfeit or that a matching matrix would prove it genuine.
    /// </summary>
    DecodeIntegrityFailed,

    Available,
    Assigned,
    Used,
    Expired,
    Blocked,
    Deactivated
}

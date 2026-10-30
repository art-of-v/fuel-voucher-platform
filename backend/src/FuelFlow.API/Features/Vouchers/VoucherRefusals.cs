using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Errors;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Vouchers;

/// <summary>
/// The stable codes a voucher refusal travels under, so a client can render the reason in the
/// operator's own language instead of reading English prose off the wire.
/// </summary>
/// <remarks>
/// One list because the endpoints that refuse the same way must not invent their own spelling: the
/// admin maps these onto translation keys. It covers the activation gate in
/// <see cref="FuelEconomicsGate"/> and the deletion guard in <see cref="BulkActionVouchersCommandHandler"/>
/// - one list, because two lists drift.
/// </remarks>
internal static class VoucherRefusalCodes
{
    /// <summary>The fuel behind the stock has no cost or no margin, so it has no price to sell at.</summary>
    public const string FuelNotPriced = "fuel_not_priced";

    /// <summary>A voucher names a fuel that is not in the catalogue at all.</summary>
    public const string FuelMissing = "fuel_missing";

    /// <summary>Some vouchers of this batch carry no supplier cost yet.</summary>
    public const string VoucherCostMissing = "voucher_cost_missing";

    /// <summary>Some of the selected vouchers have left stock and must not be archived.</summary>
    public const string VouchersNotDeletable = "vouchers_not_deletable";

    /// <summary>"Delete all" archived the stock it may and kept the vouchers that are paid for.</summary>
    public const string VoucherArchivePartial = "voucher_archive_partial";

    /// <summary>Allocation is a fulfilment step; there is no manual assign any more.</summary>
    public const string ManualAssignDisabled = "manual_assign_disabled";
}

/// <summary>
/// An activation the gate refused: the sentence for the log and for any client that has not learnt
/// the code, the code itself, and the fuels the operator has to price.
/// </summary>
internal sealed record ActivationRefusal(
    string Message,
    string Code,
    IReadOnlyList<UnpricedFuel>? UnpricedFuels = null);

/// <summary>
/// A refusal as the answer it is served: the English sentence stays in <c>detail</c> for the logs
/// and for any client that has not learnt the code, and the reason travels beside it as
/// <c>code</c>, <c>data</c> and <c>extra</c>, so the admin can say it in the operator's language.
/// </summary>
/// <remarks>
/// One mapper for every refusal that carries a code, for the same reason the gate is one gate: two
/// mappings drift, and a client that reads a code from one button and prose from the other has
/// neither. The typed parameters are the two payloads the activation gate sends; <c>data</c> and
/// <c>extra</c> are the escape hatch for a refusal that carries something else (the deletion guard's
/// counts, the voucher it stopped on and the signal that refused it).
/// </remarks>
internal static class VoucherRefusalError
{
    public static ObjectResult BadRequest(
        string? detail,
        string? code,
        IReadOnlyList<UnpricedFuel>? unpricedFuels = null,
        int? uncostedVoucherCount = null,
        IReadOnlyDictionary<string, object?>? data = null,
        IReadOnlyDictionary<string, object?>? extra = null)
    {
        // `data` is the numbers a localised sentence interpolates; the fuels are names, and they
        // travel under their own key because the envelope's `data` is documented as numeric.
        var numbers = new Dictionary<string, object?>();
        if (uncostedVoucherCount is { } count) numbers["voucherCount"] = count;
        foreach (var pair in data ?? Empty) numbers[pair.Key] = pair.Value;

        var extensions = new Dictionary<string, object?>();
        if (unpricedFuels is { Count: > 0 } fuels) extensions["unpricedFuels"] = fuels;
        foreach (var pair in extra ?? Empty) extensions[pair.Key] = pair.Value;

        return ApiError.BadRequest(
            detail,
            code,
            numbers.Count > 0 ? numbers : null,
            extensions.Count > 0 ? extensions : null);
    }

    private static readonly IReadOnlyDictionary<string, object?> Empty =
        new Dictionary<string, object?>();
}

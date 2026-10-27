using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers;

/// <summary>
/// The stable codes an activation refusal travels under, so a client can render the reason in the
/// operator's own language instead of reading English prose off the wire.
/// </summary>
/// <remarks>
/// One list because the two activation endpoints must not invent their own spelling of the same
/// refusal: the admin maps these onto translation keys.
/// </remarks>
internal static class ActivationCodes
{
    /// <summary>The fuel behind the stock has no cost or no margin, so it has no price to sell at.</summary>
    public const string FuelNotPriced = "fuel_not_priced";

    /// <summary>A voucher names a fuel that is not in the catalogue at all.</summary>
    public const string FuelMissing = "fuel_missing";

    /// <summary>Some vouchers of this batch carry no supplier cost yet.</summary>
    public const string VoucherCostMissing = "voucher_cost_missing";
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
/// The activation refusal as the answer it is served: the English sentence stays in <c>detail</c>
/// for the logs and for any client that has not learnt the code, and the reason travels beside it
/// as <c>code</c>, <c>data</c> and the fuel list, so the admin can say it in the operator's language.
/// </summary>
/// <remarks>
/// One mapper for both activation endpoints, for the same reason the gate is one gate: two mappings
/// drift, and a client that reads a code from one button and prose from the other has neither.
/// </remarks>
internal static class ActivationError
{
    public static ObjectResult BadRequest(
        string? detail,
        string? code,
        IReadOnlyList<UnpricedFuel>? unpricedFuels = null,
        int? uncostedVoucherCount = null)
    {
        // `data` is the numbers a localised sentence interpolates; the fuels are names, and they
        // travel under their own key because the envelope's `data` is documented as numeric.
        IReadOnlyDictionary<string, object?>? data = uncostedVoucherCount is { } count
            ? new Dictionary<string, object?> { ["voucherCount"] = count }
            : null;

        IReadOnlyDictionary<string, object?>? extra = unpricedFuels is { Count: > 0 } fuels
            ? new Dictionary<string, object?> { ["unpricedFuels"] = fuels }
            : null;

        return ApiError.BadRequest(detail, code, data, extra);
    }
}

/// <summary>
/// The rule that stock cannot go on the shelf while its fuel has no economics of its own, in one
/// place so the id-based activation and the batch activation cannot drift apart.
/// </summary>
/// <remarks>
/// <para>
/// The cost gate that already stood here asks about the VOUCHER: what this batch cost us. This one
/// asks about the FUEL, and the two are different questions. The price the customer is charged is
/// computed from the fuel's cost and margin (<see cref="FuelPricing.FinalPerLiterOrNull"/>), so a
/// fuel whose margin was never set has no price whatever the vouchers carry - and the sale is
/// refused at checkout, in front of the customer, on a shelf that said the fuel was there.
/// </para>
/// <para>
/// The rehearsal pool is exempt: it is never sold to a real customer, and a rehearsal fuel is
/// allowed to have no economics at all. Everything that is not explicitly QA is treated as live, so
/// a row that names no pool is priced rather than sold without a price.
/// </para>
/// </remarks>
internal static class FuelEconomicsGate
{
    /// <summary>
    /// The fuels of these vouchers that belong to the live pool.
    /// </summary>
    internal static List<Guid> LiveFuelTypeIds(IEnumerable<FuelVoucher> vouchers)
        => vouchers
            .Where(v => v.DataKind != DataKinds.Qa)
            .Select(v => v.FuelTypeId)
            .Distinct()
            .ToList();

    /// <summary>
    /// The refusal, or null when every live fuel behind these vouchers has a price of its own.
    /// </summary>
    internal static async Task<ActivationRefusal?> RefusalAsync(
        ApplicationDbContext context,
        IReadOnlyCollection<Guid> fuelTypeIds,
        CancellationToken cancellationToken)
    {
        if (fuelTypeIds.Count == 0)
        {
            return null;
        }

        var fuels = await context.FuelTypes
            .Where(f => fuelTypeIds.Contains(f.Id))
            .Select(f => new { f.Id, f.Name, f.SupplierPricePerLiter, f.MarginUahPerLiter })
            .ToListAsync(cancellationToken);

        if (fuels.Count != fuelTypeIds.Count)
        {
            return new ActivationRefusal(
                "Cannot activate - a voucher in this selection belongs to a fuel that is not in the "
                + "catalogue. Restore the fuel or remove the voucher: a voucher with no fuel behind it "
                + "has no price to sell at.",
                ActivationCodes.FuelMissing);
        }

        var priced = fuels.Select(f => (f.Name, State: FuelEconomics.State(f.SupplierPricePerLiter, f.MarginUahPerLiter)));
        var unpriced = FuelEconomics.Unpriced(priced);
        if (unpriced.Count == 0)
        {
            return null;
        }

        return new ActivationRefusal(
            FuelEconomics.ActivationRefusal(priced),
            ActivationCodes.FuelNotPriced,
            unpriced);
    }
}

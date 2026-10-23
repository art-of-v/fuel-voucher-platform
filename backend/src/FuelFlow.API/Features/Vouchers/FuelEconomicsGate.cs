using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers;

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
    internal static async Task<string?> RefusalAsync(
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
            return "Cannot activate - a voucher in this selection belongs to a fuel that is not in the "
                + "catalogue. Restore the fuel or remove the voucher: a voucher with no fuel behind it "
                + "has no price to sell at.";
        }

        var gaps = fuels
            .Select(f => (f.Name, State: FuelEconomics.State(f.SupplierPricePerLiter, f.MarginUahPerLiter)))
            .Where(f => !f.State.IsPriced)
            .ToList();

        var refusal = FuelEconomics.ActivationRefusal(gaps);
        return refusal.Length == 0 ? null : refusal;
    }
}

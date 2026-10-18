namespace FuelFlow.Features.Brands;

/// <summary>
/// Explicit allow-list projection of <see cref="FuelFlow.SharedKernel.Domain.FuelPackage"/> for the
/// anonymous <c>GET /api/packages</c> endpoint (planning #52). The per-Brand variant that
/// served the same projection was removed as client-less; the rule below applies to whatever
/// reads this type.
///
/// That endpoint previously returned the raw <c>FuelPackage</c> entity, which serialized the
/// operator's commercially-sensitive pricing to unauthenticated callers: <c>SupplierPricePerLiter</c>
/// (acquisition cost), <c>MarginUahPerLiter</c> and <c>MarginPercent</c> (markup). The fields below
/// are exactly the store-front values a shopper needs — the sale price is fine to show — and
/// deliberately EXCLUDE cost and margin. Do NOT add a cost/margin field here: the acquisition-cost /
/// COGS model must stay operator-only and must never reach a public endpoint.
///
/// <paramref name="IsAvailable"/> is a <em>can this be bought right now</em> flag, not a quantity and not
/// cost information: it is true when at least one activated (sellable) voucher backs this exact
/// brand + fuel + nominal for the caller. It reveals nothing an anonymous shopper could not work out by
/// trying to buy, and it exists so the app can grey a package out instead of taking money for fuel the
/// operator does not have. See <c>PackageStockAvailability</c> for the single definition behind it.
/// </summary>
public sealed record PublicPackageResponse(
    string Id,
    string BrandId,
    string FuelTypeId,
    string FuelName,
    decimal Liters,
    decimal Price,
    decimal OriginalPrice,
    decimal? FinalPricePerLiter,
    bool IsAvailable);

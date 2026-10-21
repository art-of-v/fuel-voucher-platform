namespace FuelFlow.Features.Brands.GetPublicFuelTypes;

/// <summary>
/// Explicit allow-list projection of <see cref="FuelFlow.SharedKernel.Domain.FuelTypeEntity"/> for the
/// anonymous, publicly cacheable <c>GET /api/Brands/fuel-types</c> endpoint (FF-31).
///
/// That endpoint previously reused <c>GetAdminFuelTypesQueryHandler</c> and returned the entity, so any
/// column later added to the table would have been published to unauthenticated callers and cached for
/// 300 seconds with no code change to review. The fields below are exactly those the mobile client
/// declares in <c>mobile/src/core/types/api.ts</c>, so the wire format is unchanged; only the audit
/// timestamps, which no client reads, are dropped.
/// </summary>
/// <remarks>
/// <c>BrandId</c> became a uuid with planning D-21 and <c>BrandSlug</c> came with it: a client holding
/// only a fuel has no other way to know which network it belongs to. The fuel's own id became a uuid
/// with it, and <c>Id</c> stays a string so the wire format a client declares does not change - the
/// value in it is the uuid now, not the natural key it used to spell.
/// </remarks>
public sealed record PublicFuelTypeResponse(
    string Id,
    Guid BrandId,
    string BrandSlug,
    string Name,
    /// <summary>
    /// The reference the customer's saving is measured against: the COLUMN price, what the network
    /// charges at the pump. Null when it is not known.
    /// </summary>
    /// <remarks>
    /// This used to be a stored column, <c>base_price</c>, kept in step with the pump price by four
    /// writers. It is computed now, because the column price next door is the same fact.
    /// </remarks>
    decimal? BasePrice,
    /// <summary>
    /// What WE charge - the cost plus the margin, capped at the column price. Null when we have no
    /// cost or no margin set, which is the state an imported fuel is in until an operator prices it.
    /// </summary>
    /// <remarks>
    /// This used to be a stored column, <c>discount_price</c>, and it was a zero on fourteen of the
    /// twenty-three fuels on the catalogue. A price of zero is a price, and it says the customer
    /// takes the fuel for nothing — so it is computed now, and "not set" is null.
    /// </remarks>
    decimal? DiscountPrice,
    /// <summary>
    /// What the network charges at the pump - the column price. Null when it is not known.
    /// </summary>
    /// <remarks>
    /// This is the number a customer sees for a fuel we cannot sell: it is what the market charges,
    /// and it is the only honest price when we have no margin of our own to quote.
    /// </remarks>
    decimal? PumpPricePerLiter,
    /// <summary>
    /// What WE charge - the cost plus the margin, capped at the pump price. Null when we have no
    /// margin set, which is the state an imported fuel is in until an operator prices it.
    /// </summary>
    decimal? FinalPricePerLiter,
    /// <summary>
    /// Whether this fuel can be bought right now: at least one of its sizes has live stock behind it
    /// in the caller's own pool.
    /// </summary>
    /// <remarks>
    /// A fuel the shop cannot sell is shown at the column price rather than at a price of ours, because
    /// a zero or a market price presented as ours is a lie the customer pays for.
    /// </remarks>
    bool IsAvailable);

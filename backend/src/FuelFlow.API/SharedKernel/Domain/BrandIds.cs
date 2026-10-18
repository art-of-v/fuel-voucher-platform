namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// The ids of the four networks the seed creates, spelled once so the seed, the tests and the QA
/// scripts all agree on them without each one inventing a literal.
/// </summary>
/// <remarks>
/// <para>
/// These are <b>not</b> derived from the slug. A UUIDv5 of <c>"okko"</c> would be reproducible from
/// the slug, which sounds better until someone renames a brand: the id would have to change with it,
/// and an id that changes when a name changes is a natural key wearing a UUID costume. These are
/// chosen once, written down, and never recomputed.
/// </para>
/// <para>
/// <b>Not a substitute for a slug lookup.</b> A network the seed does not know about — UPG arrives from
/// the admin import with its own generated id — is resolved by
/// <c>BrandSlugResolver</c> at the point of use. Nothing here grows to cover every brand; the seeded
/// four are the only ids a caller may assume exist.
/// </para>
/// </remarks>
public static class BrandIds
{
    public static readonly Guid Okko = new("11111111-1111-4111-8111-111111111111");
    public static readonly Guid Wog = new("22222222-2222-4222-8222-222222222222");
    public static readonly Guid Upg = new("33333333-3333-4333-8333-333333333333");
    public static readonly Guid Klo = new("44444444-4444-4444-8444-444444444444");
}

namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// The ids of the four networks the seed creates, derived from their slug rather than written down.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why they are derived.</b> A seeded brand's id has to be the same value in three places that
/// cannot see each other: the seed EF writes into a fresh database, the migration that gave
/// production its rows, and the code that quotes the id. A hand-written literal is a thing to
/// mistype, and a thing to disagree about - and it is also a thing that reads like data when it is
/// really a decision. Derived, the three cannot disagree: they all compute it.
/// </para>
/// <para>
/// <b>Why the slug and not the name.</b> The slug is the natural key - the code a supplier file, an
/// invoice and an operator write - and nothing in the application changes it: the update handler
/// writes the name, the colour and the wordmark, and leaves the slug alone. The name is a display
/// attribute an operator may change at any time, and an id derived from it would change under the
/// operator's hands, taking every foreign key with it. That is the failure the earlier version of
/// this file warned about, and deriving from the slug does not have it.
/// </para>
/// <para>
/// <b>What the derivation is not.</b> It is not a lookup and it is not a second identity: the slug
/// stays the natural key, unique and readable, and this is only what the id happens to be called.
/// A network created at runtime is not given a derived id - it gets a fresh
/// <c>Guid.CreateVersion7()</c>, because nothing outside the database quotes it. The four seeded
/// networks need an id every environment can compute, and only they are seeded.
/// </para>
/// <para>
/// <b>The namespace is frozen.</b> Changing it changes every id this file hands out, and
/// production's rows carry the ids the current namespace produces. A change here is a data
/// migration, not an edit - the same shape as the one that gave production these ids in the first
/// place.
/// </para>
/// </remarks>
public static class BrandIds
{
    /// <summary>
    /// The namespace every brand id is derived in. Chosen once, and never changed.
    /// </summary>
    private static readonly Guid Namespace = new("5bf9bf3c-3f76-4101-8bd6-044df9a15287");

    /// <summary>The prefix that keeps a brand id in its own namespace, away from any other name.</summary>
    private const string NamePrefix = "brands/";

    public static readonly Guid Okko = ForSlug("okko");
    public static readonly Guid Wog = ForSlug("wog");
    public static readonly Guid Upg = ForSlug("upg");
    public static readonly Guid Klo = ForSlug("klo");

    /// <summary>
    /// The id the network with this slug carries.
    /// </summary>
    /// <param name="slug">
    /// The network's own code, as <see cref="Brand.Slug"/> states it: <c>"okko"</c>, <c>"wog"</c>,
    /// <c>"klo"</c>.
    /// </param>
    /// <remarks>
    /// This is the derivation, not a lookup: it answers what the id of a network is, not whether
    /// such a network exists. A network that was created at runtime carries a different id, because
    /// only the seeded ones need an id every environment can compute.
    /// </remarks>
    public static Guid ForSlug(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        return UuidV5.Create(Namespace, NamePrefix + slug);
    }

    /// <summary>
    /// An id that is deliberately not one of the above, for the "brand is missing" arm of a test.
    /// Fixed rather than <c>Guid.NewGuid()</c> so a failure names the same id every run.
    /// </summary>
    public static readonly Guid NotFound = new("99999999-9999-4999-8999-999999999999");
}

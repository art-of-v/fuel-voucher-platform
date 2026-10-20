using System.Security.Cryptography;
using System.Text;

namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// How a fuel's id becomes a uuid.
/// </summary>
/// <remarks>
/// <para>
/// Since planning D-21 a fuel's id is an opaque uuid and the brand's own code for the grade lives in
/// <see cref="FuelTypeEntity.BrandFuelCode"/>. The id used to BE the natural key - <c>"okko-95"</c> -
/// and the column still is text, so the rows that carry the old spelling have to be converted.
/// </para>
/// <para>
/// <b>One rule, in two languages.</b> A uuid id is kept as it is. A natural key is mapped to a uuid
/// derived from its name, so the same key always lands on the same uuid - which is what lets the
/// migration (SQL) and the runtime (C#) agree about a row written before the conversion and a row
/// written after it. The rule is the same one Postgres runs in <c>md5('fuelflow-fuel:' || id)::uuid</c>,
/// and <see cref="AgreesWithPostgres"/> is the test that holds the two to it.
/// </para>
/// <para>
/// The prefix is what keeps the mapping from colliding with a uuid that happens to look like a name.
/// </para>
/// </remarks>
public static class FuelIdMapper
{
    /// <summary>
    /// What the natural key is hashed with. The migration uses the same string.
    /// </summary>
    public const string NaturalKeyPrefix = "fuelflow-fuel:";

    /// <summary>
    /// Whether the id is already a uuid and needs no mapping.
    /// </summary>
    public static bool IsUuid(string id) => Guid.TryParse(id, out _);

    /// <summary>
    /// The uuid a fuel id is stored as.
    /// </summary>
    /// <param name="id">A uuid string, or a natural key like <c>"okko-95"</c>.</param>
    public static Guid ToUuid(string id)
    {
        if (Guid.TryParse(id, out var parsed))
        {
            return parsed;
        }

        // The same bytes Postgres's md5() produces, in the same order. A uuid is big-endian and
        // .NET's Guid(byte[]) is not, so the HEX is parsed rather than the bytes reinterpreted:
        // "N" is the 32-digit form, and Guid.ParseExact reads it big-endian.
        var hex = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(NaturalKeyPrefix + id))).ToLowerInvariant();
        return Guid.ParseExact(hex, "N");
    }
}

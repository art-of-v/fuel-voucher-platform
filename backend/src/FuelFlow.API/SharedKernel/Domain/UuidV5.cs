using System.Security.Cryptography;
using System.Text;

namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// RFC 4122 version 5 uuids: a name hashed into a namespace, so the same name in the same namespace
/// always produces the same uuid and nothing else does.
/// </summary>
/// <remarks>
/// <para>
/// .NET ships <c>Guid.CreateVersion7</c> and nothing for version 5, so this is the RFC's algorithm
/// spelled out: SHA-1 over the namespace and the name, then the version and variant bits stamped
/// into the result.
/// </para>
/// <para>
/// <b>SHA-1 is not a choice here.</b> RFC 4122 defines version 5 as SHA-1, and a uuid that other
/// tools can reproduce has to be the RFC's version 5 rather than a lookalike. The hash is a naming
/// function here, not a security boundary: it decides what an id is called, not who may read what.
/// </para>
/// <para>
/// <b>The namespace is the whole point of the type.</b> The same name under two namespaces is two
/// different uuids, which is what lets one derivation be reused without colliding with anything
/// else that hashes the same words.
/// </para>
/// </remarks>
public static class UuidV5
{
    /// <summary>
    /// The RFC's DNS namespace. Kept so the test vector that proves this is the RFC's algorithm can
    /// be stated in the same terms the RFC states it.
    /// </summary>
    public static readonly Guid DnsNamespace = new("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

    /// <summary>
    /// The uuid RFC 4122 gives <c>www.example.com</c> in the DNS namespace. The test vector every
    /// implementation of this algorithm is checked against.
    /// </summary>
    public static readonly Guid DnsExampleCom = new("2ed6657d-e927-568b-95e1-2665a8aea6a2");

    /// <summary>
    /// The version 5 uuid of <paramref name="name"/> in <paramref name="namespaceId"/>.
    /// </summary>
    /// <param name="namespaceId">The namespace. The same name in another namespace is another uuid.</param>
    /// <param name="name">The name being hashed. UTF-8, with no terminator.</param>
    public static Guid Create(Guid namespaceId, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var nameBytes = Encoding.UTF8.GetBytes(name);
        var buffer = new byte[16 + nameBytes.Length];
        WriteBigEndian(buffer.AsSpan(0, 16), namespaceId);
        nameBytes.CopyTo(buffer.AsSpan(16));

        Span<byte> hash = stackalloc byte[20];
        SHA1.HashData(buffer, hash);

        Span<byte> result = stackalloc byte[16];
        hash[..16].CopyTo(result);

        // Version 5 in the high nibble of octet 6, and the RFC 4122 variant in the two high bits
        // of octet 8. Both are bits the hash owns and the format does not.
        result[6] = (byte)((result[6] & 0x0F) | 0x50);
        result[8] = (byte)((result[8] & 0x3F) | 0x80);

        return new Guid(Convert.ToHexString(result));
    }

    /// <summary>
    /// The namespace's sixteen octets, in the order the RFC hashes them.
    /// </summary>
    /// <remarks>
    /// <see cref="Guid.ToByteArray()"/> is not that order: it writes the first three fields
    /// little-endian, which is what the Windows registry wants and what the RFC does not. The
    /// hexadecimal form is the RFC's order, so that is the form used here.
    /// </remarks>
    private static void WriteBigEndian(Span<byte> destination, Guid value)
    {
        var text = value.ToString("N");
        for (var i = 0; i < 16; i++)
        {
            destination[i] = Convert.ToByte(text.Substring(i * 2, 2), 16);
        }
    }
}

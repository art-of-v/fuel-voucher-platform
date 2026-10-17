using System.Text;
using FuelFlow.Features.Vouchers.Security;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Options;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The cipher every integration test builds its context with.
/// </summary>
/// <remarks>
/// <para>
/// A fixed key, for the same reason the QA access code is fixed in the fixture: determinism. The
/// container is thrown away at the end of the run and nothing encrypted in it survives, so a
/// hard-coded key here leaks nothing that matters.
/// </para>
/// <para>
/// <b>This is the single source of the test key.</b> The fixture exports <see cref="KeyBase64"/>
/// to the environment so the application's own container - reached through HTTP in some tests -
/// builds its cipher from the same value. Two literals would be two ciphers: a voucher written by
/// a test context would fail to decrypt when an endpoint read it back, surfacing as an HTTP 500 with
/// nothing in it pointing at keys.
/// </para>
/// <para>
/// It exists as one shared instance rather than a fallback inside the context because a null cipher
/// is refused rather than defaulted, and that refusal is the point: a defaulted key in production
/// would be a key every deployment shares, and a QR payload is what a verifier trusts.
/// </para>
/// </remarks>
public static class TestQrCipher
{
    /// <summary>32 bytes, base64. Supplied to the application host through <c>QrCipher__Key</c>.</summary>
    public static string KeyBase64 { get; } = Convert.ToBase64String(Encoding.UTF8.GetBytes(Phrase));

    private const string Phrase = "integrations-qr-cipher-key-2026!";

    public static IQrCipher Instance { get; } = new QrCipher(Options.Create(new QrCipherOptions
    {
        Key = KeyBase64
    }));
}
using System.Text;
using FuelFlow.Features.Vouchers.Security;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Options;

namespace FuelFlow.UnitTests;

/// <summary>
/// The cipher every unit test builds its context with.
/// </summary>
/// <remarks>
/// A fixed key for determinism. Nothing encrypted in an in-memory provider survives the test, so this
/// leaks nothing.
///
/// It is one shared instance because a null cipher is refused rather than defaulted, and that refusal
/// is load-bearing: a defaulted key in production would be a key every deployment shares, and a QR
/// payload is what a verifier trusts. Making the parameter required on the context is what stops a
/// construction site from quietly ending up without one.
/// </remarks>
public static class TestQrCipher
{
    public static string KeyBase64 { get; } = Convert.ToBase64String(Encoding.UTF8.GetBytes(Phrase));

    private const string Phrase = "integrations-qr-cipher-key-2026!";

    public static IQrCipher Instance { get; } = new QrCipher(Options.Create(new QrCipherOptions
    {
        Key = KeyBase64
    }));
}
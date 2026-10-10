using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using FuelFlow.SharedKernel.Security;

namespace FuelFlow.UnitTests.Shared;

public sealed class AsymmetricSignatureVerifierTests
{
    private const string Payload = "{\"invoiceId\":\"abc\",\"amount\":250000}";

    private readonly AsymmetricSignatureVerifier _verifier = new();

    private static (string PublicKeyPem, string Signature) SignPayload(string payload)
    {
        using var rsa = RSA.Create(2048);
        var publicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();

        var signature = Convert.ToBase64String(
            rsa.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        return (publicKeyPem, signature);
    }

    [Fact]
    public void Verify_ValidSignature_ShouldReturnTrue()
    {
        var (publicKey, signature) = SignPayload(Payload);

        _verifier.Verify(Payload, signature, publicKey).Should().BeTrue();
    }

    [Fact]
    public void Verify_TamperedPayload_ShouldReturnFalse()
    {
        var (publicKey, signature) = SignPayload(Payload);

        _verifier.Verify(Payload + "tampered", signature, publicKey).Should().BeFalse();
    }

    [Fact]
    public void Verify_WrongSignature_ShouldReturnFalse()
    {
        var (publicKey, _) = SignPayload(Payload);
        var (_, otherSignature) = SignPayload("different payload");

        _verifier.Verify(Payload, otherSignature, publicKey).Should().BeFalse();
    }

    [Fact]
    public void Verify_WrongKey_ShouldReturnFalse()
    {
        var (_, signature) = SignPayload(Payload);
        using var otherRsa = RSA.Create(2048);

        _verifier.Verify(Payload, signature, otherRsa.ExportSubjectPublicKeyInfoPem()).Should().BeFalse();
    }

    [Fact]
    public void Verify_MalformedBase64_ShouldReturnFalse()
    {
        var (publicKey, _) = SignPayload(Payload);

        _verifier.Verify(Payload, "not-base64-!!!", publicKey).Should().BeFalse();
    }

    [Fact]
    public void Verify_RawPublicKeyWithoutPemHeaders_ShouldStillVerify()
    {
        var (publicKey, signature) = SignPayload(Payload);
        var raw = publicKey
            .Replace("-----BEGIN PUBLIC KEY-----", "")
            .Replace("-----END PUBLIC KEY-----", "")
            .Replace("\n", "")
            .Trim();

        _verifier.Verify(Payload, signature, raw).Should().BeTrue();
    }

    [Fact]
    public void Verify_Base64EncodedPem_ShouldStillVerify()
    {
        var (publicKey, signature) = SignPayload(Payload);
        var base64Pem = Convert.ToBase64String(Encoding.UTF8.GetBytes(publicKey));

        _verifier.Verify(Payload, signature, base64Pem).Should().BeTrue();
    }

    /// <summary>
    /// Pins the production failure this class had: an EC key handed to <c>RSA.ImportFromPem</c> throws
    /// <see cref="ArgumentException"/> ("no supported key formats"), not
    /// <see cref="CryptographicException"/>. Catching only the latter let it escape the method, so the
    /// ECDSA branch never ran and every Monobank webhook answered 500 - a customer pays and the order
    /// never leaves <c>PendingPayment</c>.
    /// </summary>
    /// <remarks>
    /// P-256 is available on every runtime, so this cannot pass by skipping the way the secp256k1 case
    /// below does on a runtime without that curve.
    /// </remarks>
    [Fact]
    public void Verify_EcdsaP256_ShouldVerifyOnEveryPlatform()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signature = Convert.ToBase64String(
            ecdsa.SignData(Encoding.UTF8.GetBytes(Payload), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
        var pem = ecdsa.ExportSubjectPublicKeyInfoPem();

        _verifier.Verify(Payload, signature, pem).Should().BeTrue();

        // Monobank ships the key base64-encoded (GET /api/merchant/pubkey), so the same key has to
        // verify in that form too - that is the shape production actually holds.
        var base64Pem = Convert.ToBase64String(Encoding.UTF8.GetBytes(pem));
        _verifier.Verify(Payload, signature, base64Pem).Should().BeTrue();
    }

    /// <summary>
    /// Monobank's acquiring webhooks are signed with secp256k1. This one is opportunistic: on a
    /// runtime without that curve it returns without asserting anything (Windows CNG has no
    /// secp256k1), which is why the P-256 case above carries the regression.
    /// </summary>
    [Fact]
    public void Verify_EcdsaSecp256k1_ShouldVerifyWhenCurveSupported()
    {
        ECDsa ecdsa;
        try
        {
            ecdsa = ECDsa.Create(ECCurve.CreateFromOid(new Oid("1.3.132.0.10", "secp256k1")));
        }
        catch (Exception)
        {
            return;
        }

        using (ecdsa)
        {
            var signature = Convert.ToBase64String(
                ecdsa.SignData(Encoding.UTF8.GetBytes(Payload), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
            var pem = ecdsa.ExportSubjectPublicKeyInfoPem();

            _verifier.Verify(Payload, signature, pem).Should().BeTrue();
        }
    }

    /// <summary>
    /// The startup guard reads keys through the same helper, so what it accepts and what the verifier
    /// can actually read are one decision. A truncated value is the production case: it is set, it is
    /// not a placeholder, and it fails only once verification runs — by which time the only thing
    /// completing payments is the reconciliation poll.
    /// </summary>
    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("not-a-key", false)]
    [InlineData("LS0tLS1CRUdJTiB", false)]                      // truncated base64-of-PEM
    [InlineData("-----BEGIN PUBLIC KEY-----\nMFkw\n", false)]  // PEM with no END line
    public void PublicKeyFormat_TreatsUnusableValuesAsUnreadable(string key, bool expected)
    {
        PublicKeyFormat.IsReadable(key).Should().Be(expected);
    }

    [Fact]
    public void PublicKeyFormat_ReadsBase64OfPem()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = ecdsa.ExportSubjectPublicKeyInfoPem();

        PublicKeyFormat.TryRead(Convert.ToBase64String(Encoding.UTF8.GetBytes(pem)), out var normalized).Should().BeTrue();
        normalized.Should().Contain("-----BEGIN PUBLIC KEY-----");
    }

    [Fact]
    public void PublicKeyFormat_ReadsRawPem()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = ecdsa.ExportSubjectPublicKeyInfoPem();

        PublicKeyFormat.TryRead(pem, out var normalized).Should().BeTrue();
        normalized.Should().StartWith("-----BEGIN PUBLIC KEY-----");
    }

    [Fact]
    public void PublicKeyFormat_ReadsRawDerBytes()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var der = ecdsa.ExportSubjectPublicKeyInfo();

        PublicKeyFormat.TryRead(Convert.ToBase64String(der), out var normalized).Should().BeTrue();
        normalized.Should().Contain("-----BEGIN PUBLIC KEY-----");
    }

    [Fact]
    public void PublicKeyFormat_AcceptsTheKeyWithSurroundingWhitespace()
    {
        // Env files carry a stray newline or space more often than anything else; refusing the value
        // over one would be a self-inflicted outage.
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = ecdsa.ExportSubjectPublicKeyInfoPem();

        PublicKeyFormat.TryRead($"  {pem}\n", out var normalized).Should().BeTrue();
        normalized.Should().StartWith("-----BEGIN PUBLIC KEY-----");
    }
}

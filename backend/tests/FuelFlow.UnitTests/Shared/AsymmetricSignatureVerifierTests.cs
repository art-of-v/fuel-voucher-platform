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
}

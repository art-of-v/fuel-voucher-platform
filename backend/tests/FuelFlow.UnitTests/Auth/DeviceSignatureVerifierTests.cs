using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Auth.SharedServices;
using FuelFlow.SharedKernel.Security;
using FuelFlow.UnitTests.Shared;

namespace FuelFlow.UnitTests.Auth;

/// <summary>
/// Fixed vectors produced once with OpenSSL/Node: the private keys are not in the repo, only the
/// public keys and the signatures, so these tests prove the verifier accepts real signatures rather
/// than ones it generated for itself.
/// </summary>
public sealed class DeviceSignatureVerifierTests
{
    private const string Challenge = "challenge-value";

    private const string ChallengePayloadV2 =
        "FuelFlow-DeviceSignature-v2\nchallenge\ndevice-1\nchallenge-value";

    private const string RsaPkcs1V2Signature =
        "3dDIp3BUJ0JGjl+MYW9uKwKkhz6pcXZjqnCmD20iwONUDDs2qEu4PUvBxVO+nM/7fLSnb+A6TYw1hHIkyYBHKdAjlWYywb2LPjIM5KcRZ2hOWVxluT1P3dB0dejEDTfj5jJWgSD9nPDxbUXJYbIFmZ2ItxhV1nTeXzONqCfnVDOzA3qKMTmFO17BMNO5li5panB6Uj0lJtl8I6G7AuMKVy8qne58udcUih+81TBpd7dRyiGoMT994hCsK00qj2Oy/IwRuC8MP/fbqjW/UFfV/tVbI+9k4hvLfvSytA/lTDJ0B5Uekj6+VHrc/+Q8bY37MAxGEimyqfce4ZWTcX28kQ==";

    private const string RsaPkcs1V1Signature =
        "3k/VgBAeD/+zh6iBWV+vMuEc/Hq2mdfy34Kx16gPD8KWsyb2LeEq6SM7/skqkPyHsign2VQVYEy2xWVu/EyJD1tABmCwParD0A9vnYLtRNVmp89Xp77VcRSzh1Dt1eNh8pWRVAVDbbfnZMIGPX4ZHYDeAOJAtLgKj4gvpsmmNawEKexhd/h+aXjKxZy9tGqdwK77oJcuXTqZ0pb69tXRfTERG9ULr+mnVEmkHM6VEyMEy1teJ9OJX3gPX80tfcmzXVuG83mdUf7Gm8/+ZA/ABjBp5J61ElYanXaT5f9KNw3MOIttXBnsCSIXjsiv4qmvCFdyatTc8SiWJ8JhAR1IUg==";

    private const string RsaPssV2Signature =
        "a1IDcT+CfJgxmQJqJcuS/GxVerHerqYgGUOW8vOZqq7Zx+91trUGqZnBNudvOjLeJoTF5mFiYbd5usMft+5zMppyiFbUcvJwLgTdeDYdJIDI615JfwySoj5aY2SgVf5iDe0YnHZRcbJkPpKvxqCET0303u2EiWLRsrgfYoIPVH5GgWhumvAggGoL9Sqgd5YEgoZsYtX36kq3A6/gv8Zt/lsUZAhpYg597KiQOv4DxaYLi/Be+qt7SBmmR42YWQhpCICKspVmwyxHz/2GXdPuMmXtTeOYYamWclQazCDevFObsrgXbcucBoJ3deTsIfbLVExZ/rtM5bNuOOgteCosxg==";

    private const string EcdsaDerV2Signature =
        "MEUCIBfQ2AZ/mQ0vE1pyYOeQOQlOnrL2E/QWfaHfZtiTSYhTAiEAu8npakYWiBbKEiscpp3jmRz39pkS4MHfXhejFb0LZB0=";

    private const string EcdsaP1363V2Signature =
        "QbhZQuGeRIcGCxa7V4RLDp4uYe5Hqqp1T7GQdSiyusxutRCu1zgHNES+VcxpHxXtnM7qWexV+INi/bqMK9RVyg==";

    private const string RequestRsaPublicKey = """
        -----BEGIN PUBLIC KEY-----
        MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAweUil62S6SiJbhSeaWZK
        Aby5huyhiBAaTTPnUfqAPNFqZIfxxyPPFJ9D+TCoTj1vZDCPD4xXkYZaqWVaIGqn
        cpwcNoVJLbLvcg6Cdn6lJ5l3Qrek5mifaXh076R9WRVbiCC357kZKod/oVRGLhsg
        PI9hi4R2AQQT5Hl7t+EDKcOgiibuN80SfE4dzmpKsZBer+VYFFghP7vxzip37tA/
        CqBCpPWaPmqT+Nw2dw6IYuTbyJz9i0EajK/XcYiD+8H933D9KWStho1/Us6dfgSa
        nsc7jsyLgTf95mLEJpbtvbNWv7kwoeqUxV53GsYfivZakW1+sN2qfYfW9iXQVWpq
        WQIDAQAB
        -----END PUBLIC KEY-----
        """;

    private const string RequestV2Signature =
        "SncgpKXrTUEsoiOFcpGlmy0+8ndENXQ7eccsnfm0fFqtjly7gM7EIBoNe2keDXLSV0SFZysxlug5nq95M5xeNExhVWio43/o9pBOiufyBA3hNhspVOwwDSDUIds8x9ZysomjpOlO7P4FIiM7JNfX34j7fA+rg3KuiTdSyE7+JUiyJD6tPfthYanG7KenqadC+/n8lAIxZiVUgitTP6HN2qcmenmb94RrQIq1OD/2miGWdlaHySAGtElM80858kS5THnfa20S/Pmuye/nzZI7aI88S1a8uIat//kFA1fhWeGi0TOZxvpjaXSnyGcYfXL4phVK3Mk347bVBjzyWJ2+zw==";

    private readonly DeviceSignatureVerifier _verifier = new(new AsymmetricSignatureVerifier());

    [Fact]
    public void BuildChallengePayload_Version2_IsTheDocumentedShape()
    {
        DeviceSignatureVerifier.BuildChallengePayload("device-1", Challenge, 2)
            .Should().Be(ChallengePayloadV2);
    }

    [Fact]
    public void BuildChallengePayload_Version1_StaysTheRawChallenge()
    {
        DeviceSignatureVerifier.BuildChallengePayload("device-1", Challenge, 1)
            .Should().Be(Challenge);
    }

    [Fact]
    public void BuildRequestPayload_Version2_PutsEveryFieldInAFixedSlot()
    {
        DeviceSignatureVerifier
            .BuildRequestPayload("device-1", "POST", "/api/purchases", "{\"x\":1}", "1700000000000", 2)
            .Should()
            .Be("FuelFlow-DeviceSignature-v2\nrequest\ndevice-1\nPOST\n/api/purchases\n1700000000000\n{\"x\":1}");
    }

    [Fact]
    public void BuildRequestPayload_Version1_StaysTheLegacyConcatenation()
    {
        DeviceSignatureVerifier
            .BuildRequestPayload("device-1", "POST", "/api/purchases", "{\"x\":1}", "1700000000000", 1)
            .Should()
            .Be("POST/api/purchases{\"x\":1}1700000000000");
    }

    [Fact]
    public void VerifyChallenge_RsaPkcs1V2_WithThePin()
    {
        var result = _verifier.VerifyChallenge(
            Device(DeviceTestKeys.RsaPrimary, AsymmetricSignatureAlgorithm.RsaPkcs1Sha256, 2),
            "device-1",
            Challenge,
            RsaPkcs1V2Signature);

        result.IsValid.Should().BeTrue();
        result.MatchedAlgorithm.Should().Be(AsymmetricSignatureAlgorithm.RsaPkcs1Sha256);
        result.KeyType.Should().Be("RSA");
        result.Attempts.Should().ContainSingle();
    }

    [Fact]
    public void VerifyChallenge_RsaPkcs1V1_StillVerifiesTheInstalledBase()
    {
        var result = _verifier.VerifyChallenge(
            Device(DeviceTestKeys.RsaPrimary, AsymmetricSignatureAlgorithm.RsaPkcs1Sha256, 1),
            "device-1",
            Challenge,
            RsaPkcs1V1Signature);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void VerifyChallenge_RsaPss_WhenPinnedToPss()
    {
        var result = _verifier.VerifyChallenge(
            Device(DeviceTestKeys.RsaPrimary, AsymmetricSignatureAlgorithm.RsaPssSha256, 2),
            "device-1",
            Challenge,
            RsaPssV2Signature);

        result.IsValid.Should().BeTrue();
        result.MatchedAlgorithm.Should().Be(AsymmetricSignatureAlgorithm.RsaPssSha256);
    }

    [Fact]
    public void VerifyChallenge_EcdsaDer_WhenPinnedToDer()
    {
        var result = _verifier.VerifyChallenge(
            Device(DeviceTestKeys.EcPrimary, AsymmetricSignatureAlgorithm.EcdsaSha256Der, 2),
            "device-1",
            Challenge,
            EcdsaDerV2Signature);

        result.IsValid.Should().BeTrue();
        result.KeyType.Should().Be("ECDSA");
    }

    [Fact]
    public void VerifyChallenge_EcdsaP1363_WhenPinnedToP1363()
    {
        var result = _verifier.VerifyChallenge(
            Device(DeviceTestKeys.EcPrimary, AsymmetricSignatureAlgorithm.EcdsaSha256P1363, 2),
            "device-1",
            Challenge,
            EcdsaP1363V2Signature);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void VerifyChallenge_RejectsAWrongKey()
    {
        var result = _verifier.VerifyChallenge(
            Device(DeviceTestKeys.RsaSecondary, AsymmetricSignatureAlgorithm.RsaPkcs1Sha256, 2),
            "device-1",
            Challenge,
            RsaPkcs1V2Signature);

        result.IsValid.Should().BeFalse();
        result.Failure.Should().BeNull();
    }

    [Fact]
    public void VerifyChallenge_DoesNotFallBackToAnotherSchemeWhenPinned()
    {
        // The signature is PKCS#1; the device is pinned to PSS. A lenient verifier would accept it.
        var result = _verifier.VerifyChallenge(
            Device(DeviceTestKeys.RsaPrimary, AsymmetricSignatureAlgorithm.RsaPssSha256, 2),
            "device-1",
            Challenge,
            RsaPkcs1V2Signature);

        result.IsValid.Should().BeFalse();
        result.Attempts.Should().ContainSingle()
            .Which.Algorithm.Should().Be(AsymmetricSignatureAlgorithm.RsaPssSha256);
    }

    [Fact]
    public void VerifyChallenge_DoesNotAcceptAV2SignatureAsALegacyPayload()
    {
        var result = _verifier.VerifyChallenge(
            Device(DeviceTestKeys.RsaPrimary, AsymmetricSignatureAlgorithm.RsaPkcs1Sha256, 1),
            "device-1",
            Challenge,
            RsaPkcs1V2Signature);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void VerifyChallenge_DoesNotAcceptAReplayedSignatureForADifferentChallenge()
    {
        var result = _verifier.VerifyChallenge(
            Device(DeviceTestKeys.RsaPrimary, AsymmetricSignatureAlgorithm.RsaPkcs1Sha256, 2),
            "device-1",
            "a-different-challenge",
            RsaPkcs1V2Signature);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void VerifyChallenge_DoesNotAcceptAChallengeIssuedToAnotherDevice()
    {
        var result = _verifier.VerifyChallenge(
            Device(DeviceTestKeys.RsaPrimary, AsymmetricSignatureAlgorithm.RsaPkcs1Sha256, 2),
            "device-2",
            Challenge,
            RsaPkcs1V2Signature);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void VerifyChallenge_ReportsMalformedBase64()
    {
        var result = _verifier.VerifyChallenge(
            Device(DeviceTestKeys.RsaPrimary, AsymmetricSignatureAlgorithm.RsaPkcs1Sha256, 2),
            "device-1",
            Challenge,
            "not base64!");

        result.IsValid.Should().BeFalse();
        result.Failure.Should().Be("invalid-base64");
    }

    [Fact]
    public void VerifyChallenge_LegacyRowWithoutAPin_TriesEverySchemeOnce()
    {
        var result = _verifier.VerifyChallenge(
            Device(DeviceTestKeys.RsaPrimary, null, 2),
            "device-1",
            Challenge,
            RsaPkcs1V2Signature);

        result.IsValid.Should().BeTrue();
        result.MatchedAlgorithm.Should().Be(AsymmetricSignatureAlgorithm.RsaPkcs1Sha256);
    }

    [Fact]
    public void VerifyRequest_ReconstructsTheV2PayloadFromTheDeviceRow()
    {
        var result = _verifier.VerifyRequest(
            Device(RequestRsaPublicKey, AsymmetricSignatureAlgorithm.RsaPkcs1Sha256, 2),
            "POST",
            "/api/purchases",
            "{\"x\":1}",
            "1700000000000",
            RequestV2Signature);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void VerifyRequest_DoesNotSurviveATamperedBody()
    {
        var result = _verifier.VerifyRequest(
            Device(RequestRsaPublicKey, AsymmetricSignatureAlgorithm.RsaPkcs1Sha256, 2),
            "POST",
            "/api/purchases",
            "{\"x\":999}",
            "1700000000000",
            RequestV2Signature);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Diagnose_UsesTheProductionVerifierAndReportsTheMatch()
    {
        var result = _verifier.Diagnose(ChallengePayloadV2, RsaPkcs1V2Signature, DeviceTestKeys.RsaPrimary);

        result.IsValid.Should().BeTrue();
        result.MatchedAlgorithm.Should().Be(AsymmetricSignatureAlgorithm.RsaPkcs1Sha256);
    }

    [Fact]
    public void Diagnose_WhenNothingMatches_ReportsTheSchemesItTried()
    {
        var result = _verifier.Diagnose(ChallengePayloadV2, EcdsaDerV2Signature, DeviceTestKeys.RsaPrimary);

        result.IsValid.Should().BeFalse();
        result.Attempts.Should().HaveCount(4);
        result.Attempts.Should().OnlyContain(a => !a.IsValid);
    }

    [Theory]
    [InlineData(null, "RsaPkcs1Sha256")]
    [InlineData("RsaPssSha256", "RsaPssSha256")]
    [InlineData("rsapkcs1sha256", "RsaPkcs1Sha256")]
    public void TryResolveAlgorithm_InfersOrKeepsACompatibleExplicitScheme(
        string? requested,
        string expected)
    {
        DeviceSignatureVerifier.TryResolveAlgorithm(requested, DeviceTestKeys.RsaPrimary, out var algorithm, out var error)
            .Should().BeTrue();

        error.Should().BeEmpty();
        algorithm.ToString().Should().Be(expected);
    }

    [Fact]
    public void TryResolveAlgorithm_InfersDerForAnEcKey()
    {
        DeviceSignatureVerifier.TryResolveAlgorithm(null, DeviceTestKeys.EcPrimary, out var algorithm, out _)
            .Should().BeTrue();

        algorithm.Should().Be(AsymmetricSignatureAlgorithm.EcdsaSha256Der);
    }

    [Fact]
    public void TryResolveAlgorithm_RefusesAnRsaSchemeForAnEcKey()
    {
        DeviceSignatureVerifier.TryResolveAlgorithm(
                "RsaPkcs1Sha256",
                DeviceTestKeys.EcPrimary,
                out _,
                out var error)
            .Should().BeFalse();

        error.Should().Be("SignatureAlgorithmDoesNotMatchKey");
    }

    [Fact]
    public void TryResolveAlgorithm_RefusesAnUnknownName()
    {
        DeviceSignatureVerifier.TryResolveAlgorithm("Rot13", DeviceTestKeys.RsaPrimary, out _, out var error)
            .Should().BeFalse();

        error.Should().Be("UnsupportedSignatureAlgorithm");
    }

    [Fact]
    public void TryResolveAlgorithm_RefusesAnUnreadableKey()
    {
        DeviceSignatureVerifier.TryResolveAlgorithm(null, "test-key", out _, out var error)
            .Should().BeFalse();

        error.Should().Be("InvalidPublicKey");
    }

    private static Device Device(
        string publicKey,
        AsymmetricSignatureAlgorithm? algorithm,
        int payloadVersion) =>
        new()
        {
            DeviceId = "device-1",
            PublicKey = publicKey,
            SignatureAlgorithm = algorithm,
            SignaturePayloadVersion = payloadVersion,
            Status = DeviceStatus.Active
        };
}


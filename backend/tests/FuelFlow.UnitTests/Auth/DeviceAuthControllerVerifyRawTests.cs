using FluentAssertions;
using FuelFlow.API.Features.Auth;
using FuelFlow.Features.Auth;
using FuelFlow.Features.Auth.SharedServices;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Security;
using FuelFlow.UnitTests.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FuelFlow.UnitTests.Auth;

public sealed class DeviceAuthControllerVerifyRawTests
{
    private const string Payload = "FuelFlow-DeviceSignature-v2\nchallenge\ndevice-1\nchallenge-value";

    private const string RsaPkcs1Signature =
        "3dDIp3BUJ0JGjl+MYW9uKwKkhz6pcXZjqnCmD20iwONUDDs2qEu4PUvBxVO+nM/7fLSnb+A6TYw1hHIkyYBHKdAjlWYywb2LPjIM5KcRZ2hOWVxluT1P3dB0dejEDTfj5jJWgSD9nPDxbUXJYbIFmZ2ItxhV1nTeXzONqCfnVDOzA3qKMTmFO17BMNO5li5panB6Uj0lJtl8I6G7AuMKVy8qne58udcUih+81TBpd7dRyiGoMT994hCsK00qj2Oy/IwRuC8MP/fbqjW/UFfV/tVbI+9k4hvLfvSytA/lTDJ0B5Uekj6+VHrc/+Q8bY37MAxGEimyqfce4ZWTcX28kQ==";

    [Fact]
    public void VerifyRaw_IsNotFoundWithoutDevBypass()
    {
        var controller = CreateController(devBypass: false);

        var result = controller.VerifyRaw(new VerifyRawRequest
        {
            Challenge = Payload,
            Signature = RsaPkcs1Signature,
            PublicKey = DeviceTestKeys.RsaPrimary
        });

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public void VerifyRaw_RejectsAMissingChallenge()
    {
        var controller = CreateController();

        var result = controller.VerifyRaw(new VerifyRawRequest
        {
            Challenge = " ",
            Signature = RsaPkcs1Signature,
            PublicKey = DeviceTestKeys.RsaPrimary
        });

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void VerifyRaw_ReportsTheSameAnswerTheLoginPathWouldGive()
    {
        var controller = CreateController();

        var result = controller.VerifyRaw(new VerifyRawRequest
        {
            Challenge = Payload,
            Signature = RsaPkcs1Signature,
            PublicKey = DeviceTestKeys.RsaPrimary
        });

        var body = ((OkObjectResult)result).Value.As<Dictionary<string, object?>>();
        body["valid"].Should().Be(true);
        body["method"].Should().Be("RsaPkcs1Sha256");
        body["keyType"].Should().Be("RSA");
        body["rsaPkcs1Valid"].Should().Be(true);
    }

    [Fact]
    public void VerifyRaw_ReportsEveryAttemptWhenNothingMatches()
    {
        var controller = CreateController();

        var result = controller.VerifyRaw(new VerifyRawRequest
        {
            Challenge = Payload,
            Signature = RsaPkcs1Signature,
            PublicKey = DeviceTestKeys.RsaSecondary
        });

        var body = ((OkObjectResult)result).Value.As<Dictionary<string, object?>>();
        body["valid"].Should().Be(false);
        body["method"].Should().Be("NO-MATCH");
        body["rsaPkcs1Valid"].Should().Be(false);
        body["rsaPssValid"].Should().Be(false);
        body["ecdsaDerValid"].Should().Be(false);
        body["ecdsaIeeeValid"].Should().Be(false);
    }

    [Fact]
    public void VerifyRaw_ReportsAnUnreadableKeyInsteadOfThrowing()
    {
        var controller = CreateController();

        var result = controller.VerifyRaw(new VerifyRawRequest
        {
            Challenge = Payload,
            Signature = RsaPkcs1Signature,
            PublicKey = "not-a-key"
        });

        var body = ((OkObjectResult)result).Value.As<Dictionary<string, object?>>();
        body["valid"].Should().Be(false);
        body["keyType"].Should().Be("unknown");
        body["method"].Should().Be("unreadable-key");
        body["keyFingerprint"].Should().Be("invalid-base64");
    }

    private static DeviceAuthController CreateController(bool devBypass = true) =>
        new(
            null!,
            null!,
            null!,
            null!,
            new DeviceSignatureVerifier(new AsymmetricSignatureVerifier()),
            Options.Create(new AuthOptions { DevBypass = devBypass }));
}

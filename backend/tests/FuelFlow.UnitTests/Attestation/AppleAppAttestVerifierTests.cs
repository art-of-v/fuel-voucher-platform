using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Fido2NetLib;
using Fido2NetLib.Cbor;
using Fido2NetLib.Objects;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Security.Attestation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace FuelFlow.UnitTests.Attestation;

public sealed class AppleAppAttestVerifierTests : IDisposable
{
    private const string AppId = "TB929MCW58.com.fuelflowmarketplace.www";
    private const string Production = "Production";

    private readonly ECDsa _device = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _keyId = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private readonly string _publicKey;
    private readonly AppleAppAttestVerifier _verifier;

    public AppleAppAttestVerifierTests()
    {
        _publicKey = Convert.ToBase64String(new CredentialPublicKey(_device, COSE.Algorithm.ES256).GetBytes());

        var options = new Mock<IOptionsMonitor<AttestationOptions>>();
        options.SetupGet(monitor => monitor.CurrentValue).Returns(new AttestationOptions { AppleAppId = AppId });

        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(host => host.EnvironmentName).Returns(Environments.Production);

        _verifier = new AppleAppAttestVerifier(
            options.Object,
            environment.Object,
            NullLogger<AppleAppAttestVerifier>.Instance);
    }

    public void Dispose() => _device.Dispose();

    [Fact]
    public void AnAssertionFromBeforeIos27Verifies()
    {
        var clientDataHash = RandomNumberGenerator.GetBytes(32);
        var authData = AuthData(counter: 1);

        var result = _verifier.VerifyAssertion(
            Assertion(authData, clientDataHash),
            clientDataHash,
            _keyId,
            _publicKey,
            counter: 0,
            Production);

        result.Counter.Should().Be(1);
    }

    [Theory]
    [InlineData(0x40)]
    [InlineData(0xC0)]
    public void AnIos27AssertionWithExtensionsVerifies(byte flags)
    {
        var clientDataHash = RandomNumberGenerator.GetBytes(32);
        var authData = AuthData(counter: 7, flags, extensions: Ios27Extensions());

        var result = _verifier.VerifyAssertion(
            Assertion(authData, clientDataHash),
            clientDataHash,
            _keyId,
            _publicKey,
            counter: 3,
            Production);

        result.Counter.Should().Be(7);
    }

    [Fact]
    public void AnAssertionSignedByAnotherKeyIsRefused()
    {
        var clientDataHash = RandomNumberGenerator.GetBytes(32);
        var authData = AuthData(counter: 1);

        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var act = () => _verifier.VerifyAssertion(
            Assertion(authData, clientDataHash, other),
            clientDataHash,
            _keyId,
            _publicKey,
            counter: 0,
            Production);

        act.Should().Throw<AttestationVerificationException>()
            .Which.Code.Should().Be("APPLE_ASSERTION_INVALID");
    }

    [Fact]
    public void AnAssertionForAnotherAppIsRefused()
    {
        var clientDataHash = RandomNumberGenerator.GetBytes(32);
        var authData = AuthData(counter: 1, appId: "TB929MCW58.com.example.other");

        var act = () => _verifier.VerifyAssertion(
            Assertion(authData, clientDataHash),
            clientDataHash,
            _keyId,
            _publicKey,
            counter: 0,
            Production);

        act.Should().Throw<AttestationVerificationException>()
            .Which.Code.Should().Be("APPLE_ASSERTION_INVALID");
    }

    [Fact]
    public void ACounterThatDidNotAdvanceIsRefused()
    {
        var clientDataHash = RandomNumberGenerator.GetBytes(32);
        var authData = AuthData(counter: 5);

        var act = () => _verifier.VerifyAssertion(
            Assertion(authData, clientDataHash),
            clientDataHash,
            _keyId,
            _publicKey,
            counter: 5,
            Production);

        act.Should().Throw<AttestationVerificationException>()
            .Which.Code.Should().Be("APPLE_ASSERTION_INVALID");
    }

    [Fact]
    public void BytesAfterTheCounterThatAreNotOneCborMapAreRefused()
    {
        var clientDataHash = RandomNumberGenerator.GetBytes(32);
        var authData = AuthData(counter: 1, flags: 0xC0, extensions: [0xff, 0x00, 0x01]);

        var act = () => _verifier.VerifyAssertion(
            Assertion(authData, clientDataHash),
            clientDataHash,
            _keyId,
            _publicKey,
            counter: 0,
            Production);

        act.Should().Throw<AttestationVerificationException>()
            .Which.Code.Should().Be("APPLE_ASSERTION_INVALID");
    }

    private byte[] Assertion(byte[] authData, byte[] clientDataHash, ECDsa? signer = null)
    {
        var nonce = SHA256.HashData([.. authData, .. clientDataHash]);
        var signature = (signer ?? _device)
            .SignData(nonce, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        return new CborMap
        {
            { "signature", signature },
            { "authenticatorData", authData },
        }.Encode();
    }

    private static byte[] AuthData(uint counter, byte flags = 0, string appId = AppId, byte[]? extensions = null)
    {
        var data = new byte[37];
        SHA256.HashData(Encoding.UTF8.GetBytes(appId)).CopyTo(data, 0);
        data[32] = flags;
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(33, 4), counter);

        return extensions is null ? data : [.. data, .. extensions];
    }

    private static byte[] Ios27Extensions() =>
        new CborMap
        {
            { "apple_bundle_version_01", "238" },
            { "apple_validation_category_01", new byte[] { 0x03, 0x00, 0x00, 0x00 } },
        }.Encode();
}

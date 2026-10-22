using System.Security.Cryptography;
using FluentAssertions;
using FuelFlow.Features.Attestation;
using FuelFlow.Features.Attestation.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Security.Attestation;
using FuelFlow.SharedKernel.Services;
using FuelFlow.SharedKernel.Time;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace FuelFlow.UnitTests.Attestation;

public sealed class AttestationServiceTests : IDisposable
{
    private const string DeviceId = "device-1";
    private const string OtherDeviceId = "device-2";

    private readonly ApplicationDbContext _db;
    private readonly TestTimeProvider _time = new(TestClock.Now);
    private readonly AttestationOptions _options = new()
    {
        ChallengeExpirySeconds = 120,
        AppleAppId = "TB929MCW58.com.fuelflowmarketplace.www",
        GooglePackageName = "com.artem.vashchuk.mobileappnative",
    };
    private readonly AttestationChallengeService _challenges;
    private readonly Mock<IAppleAppAttestVerifier> _apple = new();
    private readonly Mock<IPlayIntegrityVerifier> _google = new();
    private readonly AttestationService _service;

    public AttestationServiceTests()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new ApplicationDbContext(dbOptions, TestQrCipher.Instance);

        var cache = new InMemoryCacheService(NullLogger<InMemoryCacheService>.Instance, _time);
        var monitor = new Mock<IOptionsMonitor<AttestationOptions>>();
        monitor.SetupGet(value => value.CurrentValue).Returns(_options);

        _challenges = new AttestationChallengeService(
            cache,
            monitor.Object,
            _time,
            NullLogger<AttestationChallengeService>.Instance);

        _service = new AttestationService(
            _db,
            _challenges,
            _apple.Object,
            _google.Object,
            _time,
            NullLogger<AttestationService>.Instance);
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    [Fact]
    public async Task AppleRegistration_StoresTheVerifiedKey_AndIsIdempotentForTheSameDevice()
    {
        var keyIdBytes = RandomNumberGenerator.GetBytes(32);
        var keyId = Convert.ToBase64String(keyIdBytes);
        var publicKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        _apple
            .Setup(verifier => verifier.VerifyAttestationAsync(
                It.IsAny<byte[]>(),
                It.IsAny<byte[]>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppleAttestationData(keyId, publicKey, 0, "Production", null));

        var firstChallenge = await _challenges.IssueAsync(
            DeviceId,
            AttestationChallengeService.AppleAttestAction,
            string.Empty,
            CancellationToken.None);

        var first = await _service.RegisterAppleKeyAsync(
            DeviceId,
            keyId,
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
            firstChallenge.Challenge,
            CancellationToken.None);

        var secondChallenge = await _challenges.IssueAsync(
            DeviceId,
            AttestationChallengeService.AppleAttestAction,
            string.Empty,
            CancellationToken.None);

        var second = await _service.RegisterAppleKeyAsync(
            DeviceId,
            keyId,
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
            secondChallenge.Challenge,
            CancellationToken.None);

        first.Result.IsValid.Should().BeTrue();
        second.Result.IsValid.Should().BeTrue();
        second.AlreadyRegistered.Should().BeTrue();

        var stored = await _db.AttestationKeys.SingleAsync();
        stored.KeyId.Should().Be(keyId);
        stored.DeviceId.Should().Be(DeviceId);
        stored.PublicKey.Should().Be(publicKey);
        stored.Environment.Should().Be("Production");
    }

    [Fact]
    public async Task AppleRegistration_RefusesAKeyRegisteredToAnotherDevice()
    {
        var keyId = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var publicKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        _db.AttestationKeys.Add(new AttestationKey
        {
            KeyId = keyId,
            DeviceId = DeviceId,
            PublicKey = publicKey,
            Counter = 0,
            Environment = "Production",
            CreatedAt = TestClock.UtcNow,
            LastUsedAt = TestClock.UtcNow,
        });
        await _db.SaveChangesAsync();

        _apple
            .Setup(verifier => verifier.VerifyAttestationAsync(
                It.IsAny<byte[]>(),
                It.IsAny<byte[]>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppleAttestationData(keyId, publicKey, 0, "Production", null));

        var challenge = await _challenges.IssueAsync(
            OtherDeviceId,
            AttestationChallengeService.AppleAttestAction,
            string.Empty,
            CancellationToken.None);

        var outcome = await _service.RegisterAppleKeyAsync(
            OtherDeviceId,
            keyId,
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
            challenge.Challenge,
            CancellationToken.None);

        outcome.Result.IsValid.Should().BeFalse();
        outcome.Result.Code.Should().Be("ATTESTATION_KEY_EXISTS");
    }

    [Fact]
    public async Task AppleAssertion_AtTheStoredCounter_IsAReplay_AndDoesNotMoveTheCounter()
    {
        var keyIdBytes = RandomNumberGenerator.GetBytes(32);
        var keyId = Convert.ToBase64String(keyIdBytes);
        var publicKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        _db.AttestationKeys.Add(new AttestationKey
        {
            KeyId = keyId,
            DeviceId = DeviceId,
            PublicKey = publicKey,
            Counter = 5,
            Environment = "Production",
            CreatedAt = TestClock.UtcNow,
            LastUsedAt = TestClock.UtcNow,
        });
        await _db.SaveChangesAsync();

        const string body = "{\"amount\":1}";
        const string method = "POST";
        const string path = "/api/purchases";
        var requestHash = AttestationRequestHasher.Compute(method, path, body);

        var challenge = await _challenges.IssueAsync(
            DeviceId,
            AttestationChallengeService.MobileRequestAction,
            requestHash,
            CancellationToken.None);

        _apple
            .Setup(verifier => verifier.VerifyAssertion(
                It.IsAny<byte[]>(),
                It.IsAny<byte[]>(),
                keyId,
                publicKey,
                5,
                "Production"))
            .Returns(new AppleAssertionData(5));

        var result = await _service.VerifyAsync(
            method,
            path,
            body,
            DeviceId,
            "ios",
            new AttestationProof(
                "apple",
                challenge.Challenge,
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                keyId),
            CancellationToken.None);

        result.Code.Should().Be("ATTESTATION_REPLAY");
        (await _db.AttestationKeys.SingleAsync()).Counter.Should().Be(5);
    }

    [Fact]
    public async Task UnknownAppleKey_IsRejectedAfterTheChallengeIsConsumed()
    {
        var keyId = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var requestHash = AttestationRequestHasher.Compute("POST", "/api/purchases", "{}");

        var challenge = await _challenges.IssueAsync(
            DeviceId,
            AttestationChallengeService.MobileRequestAction,
            requestHash,
            CancellationToken.None);

        var result = await _service.VerifyAsync(
            "POST",
            "/api/purchases",
            "{}",
            DeviceId,
            "ios",
            new AttestationProof(
                "apple",
                challenge.Challenge,
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                keyId),
            CancellationToken.None);

        result.Code.Should().Be("ATTESTATION_KEY_UNKNOWN");

        var replayed = await _service.VerifyAsync(
            "POST",
            "/api/purchases",
            "{}",
            DeviceId,
            "ios",
            new AttestationProof(
                "apple",
                challenge.Challenge,
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                keyId),
            CancellationToken.None);

        replayed.Code.Should().Be("ATTESTATION_CHALLENGE_INVALID");
    }

    [Fact]
    public async Task GoogleProof_IsBoundToTheRequestAndAcceptedWhenTheVerifierAgrees()
    {
        const string body = "{\"amount\":2}";
        const string method = "POST";
        const string path = "/api/purchases";
        var requestHash = AttestationRequestHasher.Compute(method, path, body);

        var challenge = await _challenges.IssueAsync(
            DeviceId,
            AttestationChallengeService.MobileRequestAction,
            requestHash,
            CancellationToken.None);

        var expectedHash = AttestationRequestHasher.ComputeProofRequestHash(
            challenge.Challenge,
            requestHash);

        _google
            .Setup(verifier => verifier.VerifyAsync("token", expectedHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlayIntegrityResult.Valid(
                "PLAY_RECOGNIZED",
                "MEETS_DEVICE_INTEGRITY",
                false));

        var result = await _service.VerifyAsync(
            method,
            path,
            body,
            DeviceId,
            "android",
            new AttestationProof("google", challenge.Challenge, "token", null),
            CancellationToken.None);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ClientPlatform_MustMatchTheProofProvider()
    {
        var result = await _service.VerifyAsync(
            "POST",
            "/api/purchases",
            "{}",
            DeviceId,
            "android",
            new AttestationProof("apple", "challenge", "proof", "key"),
            CancellationToken.None);

        result.Code.Should().Be("ATTESTATION_PROVIDER_MISMATCH");
    }
}

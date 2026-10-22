using FluentAssertions;
using FuelFlow.Features.Attestation;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Services;
using FuelFlow.SharedKernel.Time;
using FuelFlow.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace FuelFlow.UnitTests.Attestation;

public sealed class AttestationChallengeServiceTests
{
    private const string DeviceId = "device-1";
    private const string RequestHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private readonly TestTimeProvider _time = new(TestClock.Now);
    private readonly InMemoryCacheService _cache;
    private readonly AttestationChallengeService _service;

    public AttestationChallengeServiceTests()
    {
        _cache = new InMemoryCacheService(NullLogger<InMemoryCacheService>.Instance, _time);
        var options = new Mock<IOptionsMonitor<AttestationOptions>>();
        options.SetupGet(monitor => monitor.CurrentValue).Returns(new AttestationOptions
        {
            ChallengeExpirySeconds = 120,
        });

        _service = new AttestationChallengeService(
            _cache,
            options.Object,
            _time,
            NullLogger<AttestationChallengeService>.Instance);
    }

    [Fact]
    public async Task Challenge_IsUsableOnce()
    {
        var issued = await _service.IssueAsync(
            DeviceId,
            AttestationChallengeService.MobileRequestAction,
            RequestHash,
            CancellationToken.None);

        var first = await _service.ConsumeAsync(
            issued.Challenge,
            DeviceId,
            AttestationChallengeService.MobileRequestAction,
            RequestHash,
            CancellationToken.None);

        var second = await _service.ConsumeAsync(
            issued.Challenge,
            DeviceId,
            AttestationChallengeService.MobileRequestAction,
            RequestHash,
            CancellationToken.None);

        first.IsValid.Should().BeTrue();
        second.Status.Should().Be(ChallengeStatus.Missing);
    }

    [Fact]
    public async Task WrongDevice_BurnsTheChallenge()
    {
        var issued = await _service.IssueAsync(
            DeviceId,
            AttestationChallengeService.MobileRequestAction,
            RequestHash,
            CancellationToken.None);

        var wrongDevice = await _service.ConsumeAsync(
            issued.Challenge,
            "device-2",
            AttestationChallengeService.MobileRequestAction,
            RequestHash,
            CancellationToken.None);

        var correctDevice = await _service.ConsumeAsync(
            issued.Challenge,
            DeviceId,
            AttestationChallengeService.MobileRequestAction,
            RequestHash,
            CancellationToken.None);

        wrongDevice.Status.Should().Be(ChallengeStatus.DeviceMismatch);
        correctDevice.Status.Should().Be(ChallengeStatus.Missing);
    }

    [Fact]
    public async Task WrongRequestOrAction_IsRejected()
    {
        var requestBound = await _service.IssueAsync(
            DeviceId,
            AttestationChallengeService.MobileRequestAction,
            RequestHash,
            CancellationToken.None);

        var wrongRequest = await _service.ConsumeAsync(
            requestBound.Challenge,
            DeviceId,
            AttestationChallengeService.MobileRequestAction,
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            CancellationToken.None);

        var apple = await _service.IssueAsync(
            DeviceId,
            AttestationChallengeService.AppleAttestAction,
            string.Empty,
            CancellationToken.None);

        var wrongAction = await _service.ConsumeAsync(
            apple.Challenge,
            DeviceId,
            AttestationChallengeService.MobileRequestAction,
            null,
            CancellationToken.None);

        wrongRequest.Status.Should().Be(ChallengeStatus.RequestMismatch);
        wrongAction.Status.Should().Be(ChallengeStatus.ActionMismatch);
    }

    [Fact]
    public async Task Challenge_ExpiresAfterTheConfiguredTtl()
    {
        var issued = await _service.IssueAsync(
            DeviceId,
            AttestationChallengeService.MobileRequestAction,
            RequestHash,
            CancellationToken.None);

        _time.Advance(TimeSpan.FromSeconds(121));

        var result = await _service.ConsumeAsync(
            issued.Challenge,
            DeviceId,
            AttestationChallengeService.MobileRequestAction,
            RequestHash,
            CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Status.Should().BeOneOf(ChallengeStatus.Missing, ChallengeStatus.Expired);
    }

    [Fact]
    public async Task IssuedChallenge_IsUrlSafeAndExpiresInTheFuture()
    {
        var issued = await _service.IssueAsync(
            DeviceId,
            AttestationChallengeService.MobileRequestAction,
            RequestHash,
            CancellationToken.None);

        issued.Challenge.Should().HaveLength(43);
        issued.Challenge.Should().NotContain("=").And.NotContain("+").And.NotContain("/");
        issued.ExpiresAt.Should().BeAfter(TestClock.Now);
    }
}

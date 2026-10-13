using FluentAssertions;
using FuelFlow.Features.Feedback;
using Xunit;

namespace FuelFlow.UnitTests.Feedback;

/// <summary>
/// The deep-link payload is the only thing that says "this Telegram chat belongs to this FuelFlow
/// account". Everything else about the feedback channel treats the chat as anonymous, so these
/// tests are the security boundary, not a convenience check.
/// </summary>
public class FeedbackDeepLinkTests
{
    private const string Key = "a-signing-key-that-is-long-enough";

    private static readonly TimeSpan Ttl = TimeSpan.FromDays(30);

    private static readonly DateTime Now = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ASignedPayloadVerifiesBackToTheSameUser()
    {
        var userId = Guid.CreateVersion7();

        var payload = FeedbackDeepLink.CreatePayload(userId, Key, Ttl, Now);

        FeedbackDeepLink.VerifyPayload(payload, Key, Ttl, Now).Should().Be(userId);
    }

    [Fact]
    public void APayloadFitsInsideTelegramSixtyFourCharacterBudget()
    {
        var payload = FeedbackDeepLink.CreatePayload(Guid.CreateVersion7(), Key, Ttl, Now);

        // Telegram only accepts [A-Za-z0-9_-] in a /start payload and caps it at 64. A GUID in
        // dashed form would already be 36, so the signature is what has to fit.
        payload.Length.Should().BeLessThanOrEqualTo(64);
        payload.Should().MatchRegex("^[A-Za-z0-9_.-]+$");
    }

    [Fact]
    public void ATamperedUserIdIsRejected()
    {
        var payload = FeedbackDeepLink.CreatePayload(Guid.CreateVersion7(), Key, Ttl, Now);
        var parts = payload.Split('.');

        // Swap in a different account while keeping the original signature.
        var forged = $"{Guid.CreateVersion7():N}.{parts[1]}.{parts[2]}";

        FeedbackDeepLink.VerifyPayload(forged, Key, Ttl, Now).Should().BeNull();
    }

    [Fact]
    public void ATamperedTimestampIsRejected()
    {
        var payload = FeedbackDeepLink.CreatePayload(Guid.CreateVersion7(), Key, Ttl, Now);
        var parts = payload.Split('.');

        // A timestamp that would otherwise pass every other check: still inside the TTL, not in the
        // future. Only the MAC can reject it, which is the point - the signature covers the time,
        // so a link cannot be kept alive by editing the date on it.
        var edited = new DateTimeOffset(Now.AddSeconds(-600)).ToUnixTimeSeconds();
        var refreshed = $"{parts[0]}.{edited}.{parts[2]}";

        FeedbackDeepLink.VerifyPayload(refreshed, Key, Ttl, Now).Should().BeNull();
    }

    [Fact]
    public void TwoLinksForTheSameUserDiffer()
    {
        // Otherwise the timestamp is decorative: an identical pair would mean a leaked link is
        // reusable for the whole TTL regardless of when it was issued.
        var userId = Guid.CreateVersion7();

        var first = FeedbackDeepLink.CreatePayload(userId, Key, Ttl, Now);
        var later = FeedbackDeepLink.CreatePayload(userId, Key, Ttl, Now.AddHours(3));

        first.Should().NotBe(later);
    }

    [Fact]
    public void APayloadSignedWithAnotherKeyIsRejected()
    {
        var payload = FeedbackDeepLink.CreatePayload(Guid.CreateVersion7(), Key, Ttl, Now);

        FeedbackDeepLink.VerifyPayload(payload, "a-different-key-that-is-long-enough", Ttl, Now)
            .Should().BeNull();
    }

    [Fact]
    public void AnExpiredPayloadIsRejected()
    {
        var userId = Guid.CreateVersion7();
        var payload = FeedbackDeepLink.CreatePayload(userId, Key, Ttl, Now);

        FeedbackDeepLink.VerifyPayload(payload, Key, Ttl, Now.AddDays(31)).Should().BeNull();
    }

    [Fact]
    public void APayloadFromTheFutureIsRejected()
    {
        var payload = FeedbackDeepLink.CreatePayload(Guid.CreateVersion7(), Key, Ttl, Now);

        // Beyond the one-minute skew allowance.
        FeedbackDeepLink.VerifyPayload(payload, Key, Ttl, Now.AddMinutes(-5)).Should().BeNull();
    }

    [Fact]
    public void SmallClockSkewDoesNotInvalidateALink()
    {
        // A phone whose clock is a couple of minutes fast must not be handed a dead link.
        var payload = FeedbackDeepLink.CreatePayload(Guid.CreateVersion7(), Key, Ttl, Now);

        FeedbackDeepLink.VerifyPayload(payload, Key, Ttl, Now.AddSeconds(-30)).Should().NotBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-dots-at-all")]
    [InlineData("only.two")]
    [InlineData("a.b.c.d")]
    [InlineData("not-a-guid.1700000000.signature")]
    [InlineData("..")]
    public void AMalformedPayloadIsRejectedRatherThanThrowing(string? payload)
    {
        var act = () => FeedbackDeepLink.VerifyPayload(payload, Key, Ttl, Now);

        act.Should().NotThrow();
        FeedbackDeepLink.VerifyPayload(payload, Key, Ttl, Now).Should().BeNull();
    }

    [Fact]
    public void AnOversizedPayloadIsRejectedWithoutInspectingIt()
    {
        var act = () => FeedbackDeepLink.VerifyPayload(new string('a', 65), Key, Ttl, Now);

        act.Should().NotThrow();
    }

    [Fact]
    public void ASigningKeyWithoutEntropyIsRefused()
    {
        // A short key would make every signature brute-forceable, so it fails at configuration
        // time rather than quietly producing weak links.
        var act = () => FeedbackDeepLink.CreatePayload(Guid.CreateVersion7(), "short", Ttl, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AnEmptySigningKeyIsRefused()
    {
        var act = () => FeedbackDeepLink.CreatePayload(Guid.CreateVersion7(), "   ", Ttl, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("palne_feedback_bot")]
    [InlineData("@palne_feedback_bot")]
    public void TheDeepLinkPointsAtTheConfiguredBotWithTheStartPayload(string username)
    {
        var url = FeedbackDeepLink.BuildDeepLink(username, "abc.def");

        url.Should().Be("https://t.me/palne_feedback_bot?start=abc.def");
    }

    [Theory]
    [InlineData("", "abc")]
    [InlineData("bot", "")]
    [InlineData(null, "abc")]
    /// <summary>
    /// Nullable because the null row below is the point of the test, not an accident: an unconfigured
    /// bot username reaches <c>BuildDeepLink</c> exactly that way, and the method's
    /// <c>IsNullOrWhiteSpace</c> guard is what turns it into "no link" rather than a link that opens
    /// nothing. Declaring the parameters non-nullable asserted a contract the callers do not honour.
    /// </summary>
    public void ADeepLinkIsNotBuiltWithoutBothParts(string? username, string? payload)
    {
        FeedbackDeepLink.BuildDeepLink(username, payload).Should().BeNull();
    }
}
using FluentAssertions;
using FuelFlow.SharedKernel.Security;

namespace FuelFlow.UnitTests.Auth;

/// <summary>
/// The TOTP primitive (RFC 6238) against the RFC's own test vectors.
/// </summary>
/// <remarks>
/// The vectors are the reason this file exists: a hand-rolled HMAC is easy to get subtly wrong -
/// a counter that is not big-endian, a truncation that reads the wrong nibble, a digit count that
/// does not match what the authenticator app shows - and every one of those mistakes produces
/// codes that are wrong in a way no unit test of "the code is six digits" would ever notice. The
/// RFC publishes the expected values for a known secret and known instants, so the implementation
/// is pinned to them.
/// </remarks>
public sealed class TotpTests
{
    /// <summary>
    /// The RFC's seed: the ASCII string "12345678901234567890", twenty bytes.
    /// </summary>
    private const string RfcSeed = "12345678901234567890";

    /// <summary>
    /// The same secret in Base32, which is the form an authenticator app reads.
    /// </summary>
    private const string RfcSeedBase32 = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    [Theory]
    // The RFC's eight-digit values, and the six-digit codes they truncate to. The RFC publishes
    // eight digits because that is what its reference implementation prints; six is what every
    // authenticator app shows, and the last six digits of the eight are the same number.
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void ComputeCode_MatchesTheRfcTestVectors(long unixSeconds, string expected)
    {
        var at = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;

        var code = Totp.ComputeCode(RfcSeedBase32, at);

        code.Should().Be(expected);
    }

    [Fact]
    public void TheSecretIsReadInBase32()
    {
        // The value an authenticator app is given. If the alphabet or the padding changed, the
        // app would refuse the secret - and the enrolment would fail on the user's phone, not in
        // a test.
        Totp.EncodeBase32(System.Text.Encoding.ASCII.GetBytes(RfcSeed)).Should().Be(RfcSeedBase32);
    }

    [Fact]
    public void DecodeBase32_RoundTripsTheSecret()
    {
        var decoded = Totp.DecodeBase32(RfcSeedBase32);

        decoded.Should().Equal(System.Text.Encoding.ASCII.GetBytes(RfcSeed));
    }

    [Fact]
    public void DecodeBase32_IsLenientAboutHowAHumanTypesIt()
    {
        // A secret is typed by hand into an app: lower case, spaces and padding all show up.
        var decoded = Totp.DecodeBase32("gezd gnbv gy3t qojq gezd gnbv gy3t qojq");

        decoded.Should().Equal(System.Text.Encoding.ASCII.GetBytes(RfcSeed));
    }

    [Fact]
    public void DecodeBase32_RefusesSomethingThatIsNotBase32()
    {
        var act = () => Totp.DecodeBase32("not-base32!");

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void GenerateSecret_ProducesTwentyBytesInBase32()
    {
        var secret = Totp.GenerateSecret();

        Totp.DecodeBase32(secret).Should().HaveCount(Totp.SecretSizeBytes);
        secret.Should().NotBe(Totp.GenerateSecret(), "two secrets must not be the same secret");
    }

    [Fact]
    public void VerifyCode_AcceptsTheCodeOfTheCurrentStep()
    {
        var secret = Totp.DecodeBase32(RfcSeedBase32);
        var at = DateTimeOffset.FromUnixTimeSeconds(1111111109).UtcDateTime;

        Totp.VerifyCode(secret, "081804", at).Should().BeTrue();
    }

    [Fact]
    public void VerifyCode_AcceptsTheNeighbouringStepsAndRefusesTheRest()
    {
        // A phone's clock and the server's clock disagree by seconds, and a code refused at a
        // 30-second boundary locks a staff member out of the panel. One step either side is
        // accepted; two steps away is another code and is refused.
        var secret = Totp.DecodeBase32(RfcSeedBase32);
        var at = DateTimeOffset.FromUnixTimeSeconds(1111111109).UtcDateTime;

        Totp.VerifyCode(secret, Totp.ComputeCode(secret, at.AddSeconds(-Totp.PeriodSeconds)), at).Should().BeTrue();
        Totp.VerifyCode(secret, Totp.ComputeCode(secret, at.AddSeconds(Totp.PeriodSeconds)), at).Should().BeTrue();
        Totp.VerifyCode(secret, Totp.ComputeCode(secret, at.AddSeconds(2 * Totp.PeriodSeconds)), at).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("abcdef")]
    [InlineData("12345a")]
    public void VerifyCode_RefusesAnythingThatIsNotSixDigits(string code)
    {
        var secret = Totp.DecodeBase32(RfcSeedBase32);
        var at = DateTimeOffset.FromUnixTimeSeconds(1111111109).UtcDateTime;

        Totp.VerifyCode(secret, code, at).Should().BeFalse();
    }

    [Fact]
    public void TheProvisioningUri_CarriesTheSecretAndTheParameters()
    {
        var uri = Totp.BuildProvisioningUri("FuelFlow", "+380991112233", RfcSeedBase32);

        uri.Should().StartWith("otpauth://totp/FuelFlow:");
        uri.Should().Contain($"secret={RfcSeedBase32}");
        uri.Should().Contain("issuer=FuelFlow");
        uri.Should().Contain("digits=6");
        uri.Should().Contain("period=30");
    }
}

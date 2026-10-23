using FluentAssertions;
using FuelFlow.Features.Auth.Security;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Security;

namespace FuelFlow.UnitTests.Auth;

/// <summary>
/// The storage contract for a verification code (planning #682): a six-digit code is not secret on
/// its own, so the digest is keyed with a server-side pepper and compared in constant time. These
/// tests pin the format the rest of the code and the database column rely on, and the two things a
/// future refactor is most likely to undo - the pepper's participation and the version marker that
/// makes an older digest recognisable instead of merely wrong.
/// </summary>
public sealed class OtpCodeHasherTests
{
    private const string Pepper = AuthOptions.DevelopmentOtpPepper;

    [Fact]
    public void Hash_ShouldCarryTheVersionPrefix_AndFitTheStoredColumn()
    {
        var hash = OtpCodeHasher.Hash("123456", Pepper);

        hash.Should().StartWith(OtpCodeHasher.PepperedPrefix);
        hash.Length.Should().BeLessThanOrEqualTo(64,
            "verification_codes.code is capped at 64 characters; a hex digest with the prefix would not fit");
        hash.Should().NotContain("123456");
    }

    [Fact]
    public void Hash_ShouldBeDeterministic_ForTheSamePepper()
    {
        OtpCodeHasher.Hash("123456", Pepper).Should().Be(OtpCodeHasher.Hash("123456", Pepper));
    }

    [Fact]
    public void Hash_ShouldDependOnThePepper()
    {
        OtpCodeHasher.Hash("123456", Pepper).Should()
            .NotBe(OtpCodeHasher.Hash("123456", Pepper + "-another"));
    }

    [Fact]
    public void Hash_ShouldNotBeTheBareSha256Digest()
    {
        // The exact failure #682 fixed: a bare digest is a million-guess search against a leaked
        // table. If this ever starts passing by being equal, the pepper has quietly stopped
        // participating while all the tests that only round-trip still go green.
        OtpCodeHasher.Hash("123456", Pepper).Should().NotBe(SecretsHasher.Hash("123456"));
    }

    [Fact]
    public void Hash_WithABlankPepper_ShouldRefuseToProduceADigest()
    {
        var hash = () => OtpCodeHasher.Hash("123456", " ");

        hash.Should().Throw<InvalidOperationException>()
            .WithMessage("*pepper*");
    }

    [Fact]
    public void Verify_ShouldAcceptTheMatchingCode()
    {
        var hash = OtpCodeHasher.Hash("123456", Pepper);

        OtpCodeHasher.Verify(hash, "123456", Pepper).Should().BeTrue();
    }

    [Fact]
    public void Verify_ShouldRejectAWrongCode()
    {
        var hash = OtpCodeHasher.Hash("123456", Pepper);

        OtpCodeHasher.Verify(hash, "123457", Pepper).Should().BeFalse();
    }

    [Fact]
    public void Verify_ShouldRejectADigestMadeWithAnotherPepper()
    {
        var hash = OtpCodeHasher.Hash("123456", Pepper);

        OtpCodeHasher.Verify(hash, "123456", Pepper + "-another").Should().BeFalse();
    }

    [Fact]
    public void Verify_ShouldRejectALegacyUnpepperedDigest_EvenForTheRightCode()
    {
        var legacy = SecretsHasher.Hash("123456");

        OtpCodeHasher.IsPeppered(legacy).Should().BeFalse();
        OtpCodeHasher.Verify(legacy, "123456", Pepper).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-digest")]
    [InlineData("v2:not-base64")]
    public void Verify_ShouldTreatAMalformedDigestAsNoMatch(string? storedHash)
    {
        OtpCodeHasher.Verify(storedHash, "123456", Pepper).Should().BeFalse();
    }
}

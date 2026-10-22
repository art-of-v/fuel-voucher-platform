using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using FuelFlow.SharedKernel.Security.Attestation;
using Microsoft.AspNetCore.Http;

namespace FuelFlow.UnitTests.Attestation;

public sealed class AttestationProtocolTests
{
    [Fact]
    public void Compute_IsStable_AndChangesWithEveryPart()
    {
        var hash = AttestationRequestHasher.Compute("post", "/api/purchases", "{\"amount\":1}");

        hash.Should().Be(AttestationRequestHasher.Compute("POST", "/api/purchases", "{\"amount\":1}"));
        hash.Should().HaveLength(43);
        hash.Should().NotContain("=").And.NotContain("+").And.NotContain("/");
        hash.Should().NotBe(AttestationRequestHasher.Compute("PUT", "/api/purchases", "{\"amount\":1}"));
        hash.Should().NotBe(AttestationRequestHasher.Compute("POST", "/api/purchases/", "{\"amount\":1}"));
        hash.Should().NotBe(AttestationRequestHasher.Compute("POST", "/api/purchases", "{\"amount\":2}"));
    }

    [Fact]
    public void ComputeProofRequestHash_IsBase64UrlSha256OfChallengeColonHash()
    {
        var challenge = "challenge-value";
        var requestHash = "request-hash-value";

        var expected = Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes($"{challenge}:{requestHash}")));

        AttestationRequestHasher.ComputeProofRequestHash(challenge, requestHash).Should().Be(expected);
    }

    [Fact]
    public void DecodeBase64_AcceptsStandardAndWebSafeUnpaddedValues()
    {
        var bytes = new byte[] { 1, 2, 3, 250, 251, 252 };

        AttestationEncoding.DecodeBase64(Convert.ToBase64String(bytes)).Should().Equal(bytes);
        AttestationEncoding.DecodeBase64(Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('='))
            .Should().Equal(bytes);
    }

    [Fact]
    public void IsBase64UrlSha256_RejectsValuesThatAreNot43UrlSafeCharacters()
    {
        AttestationEncoding.IsBase64UrlSha256(new string('a', 43)).Should().BeTrue();
        AttestationEncoding.IsBase64UrlSha256(new string('a', 42)).Should().BeFalse();
        AttestationEncoding.IsBase64UrlSha256(new string('a', 44)).Should().BeFalse();
        AttestationEncoding.IsBase64UrlSha256(new string('a', 42) + "+").Should().BeFalse();
        AttestationEncoding.IsBase64UrlSha256(new string('a', 42) + "/").Should().BeFalse();
        AttestationEncoding.IsBase64UrlSha256(null).Should().BeFalse();
    }

    [Theory]
    [InlineData("/api/purchases")]
    [InlineData("/api/purchases/")]
    [InlineData("//api//purchases//")]
    [InlineData("/api/purchases/bulk")]
    [InlineData("/api/purchases/renew")]
    [InlineData("/api/auth/send-code")]
    [InlineData("/api/auth/verify")]
    [InlineData("/api/vouchers/abc/mark-used")]
    [InlineData("/api/vouchers/ABC/restore")]
    public void IsProtected_MatchesTheMoneyAndAuthEndpoints(string path)
    {
        AttestationPathMatcher.IsProtected(new PathString(path), ProtectedEndpoints).Should().BeTrue();
    }

    [Theory]
    [InlineData("/api/purchases-other")]
    [InlineData("/api/vouchers/abc/other")]
    [InlineData("/api/vouchers/abc/mark-used/extra")]
    [InlineData("/api/devices")]
    [InlineData("/api/attestation/challenge")]
    public void IsProtected_DoesNotMatchUnrelatedPaths(string path)
    {
        AttestationPathMatcher.IsProtected(new PathString(path), ProtectedEndpoints).Should().BeFalse();
    }

    [Theory]
    [InlineData("/api/devices")]
    [InlineData("/api/devices/")]
    [InlineData("/api/devices/abc")]
    [InlineData("/api/devices/abc/rotate")]
    public void IsProtected_TrailingWildcardMatchesThePrefixAndItsChildren(string path)
    {
        AttestationPathMatcher.IsProtected(new PathString(path), ["/api/devices/*"]).Should().BeTrue();
    }

    private static readonly string[] ProtectedEndpoints =
    [
        "/api/auth/send-code",
        "/api/auth/verify",
        "/api/purchases",
        "/api/purchases/bulk",
        "/api/purchases/renew",
        "/api/vouchers/*/mark-used",
        "/api/vouchers/*/restore",
    ];

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

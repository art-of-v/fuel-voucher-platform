using FluentAssertions;
using FuelFlow.SharedKernel.Domain;
using Xunit;

namespace FuelFlow.UnitTests.SharedKernel;

/// <summary>
/// The RFC 4122 version 5 uuid, spelled out because .NET ships no factory for it.
/// </summary>
/// <remarks>
/// The only test that matters here is the first one. Everything else in the codebase that derives
/// an id rests on this being the RFC's algorithm and not a lookalike: an id that other tools
/// cannot reproduce is an id nobody can check, and the one value everybody checks it against is
/// the RFC's own test vector.
/// </remarks>
public sealed class UuidV5Tests
{
    [Fact]
    public void It_produces_the_rfc_test_vector()
    {
        // RFC 4122, appendix: the DNS namespace and "www.example.com" hash to this uuid.
        UuidV5.Create(UuidV5.DnsNamespace, "www.example.com")
            .Should().Be(UuidV5.DnsExampleCom);
    }

    [Fact]
    public void The_same_name_always_produces_the_same_uuid()
    {
        UuidV5.Create(UuidV5.DnsNamespace, "www.example.com")
            .Should().Be(UuidV5.Create(UuidV5.DnsNamespace, "www.example.com"));
    }

    [Fact]
    public void Two_namespaces_separate_the_same_name()
    {
        UuidV5.Create(UuidV5.DnsNamespace, "www.example.com")
            .Should().NotBe(UuidV5.Create(Guid.NewGuid(), "www.example.com"));
    }

    [Fact]
    public void Two_names_produce_two_uuids()
    {
        UuidV5.Create(UuidV5.DnsNamespace, "www.example.com")
            .Should().NotBe(UuidV5.Create(UuidV5.DnsNamespace, "www.example.org"));
    }

    [Fact]
    public void The_result_is_a_version_5_uuid()
    {
        var bytes = UuidV5.Create(UuidV5.DnsNamespace, "www.example.com").ToByteArray();

        // The version is the high nibble of octet 6 in the RFC's octet order, which the Guid's
        // little-endian layout puts at index 7.
        (bytes[7] & 0xF0).Should().Be(0x50);
    }

    [Fact]
    public void The_result_carries_the_rfc_variant()
    {
        var bytes = UuidV5.Create(UuidV5.DnsNamespace, "www.example.com").ToByteArray();

        // The variant is the two high bits of octet 8, which the little-endian layout puts at
        // index 8.
        (bytes[8] & 0xC0).Should().Be(0x80);
    }

    [Fact]
    public void It_refuses_a_name_that_is_not_one()
    {
        var act = () => UuidV5.Create(UuidV5.DnsNamespace, "   ");

        act.Should().Throw<ArgumentException>();
    }
}

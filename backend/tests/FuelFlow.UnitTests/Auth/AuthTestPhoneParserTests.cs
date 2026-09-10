using FluentAssertions;
using FuelFlow.SharedKernel.Options;

namespace FuelFlow.UnitTests.Auth;

public sealed class AuthTestPhoneParserTests
{
    [Fact]
    public void Parse_NullOrEmpty_ReturnsEmpty()
    {
        AuthOptions.ParseTestPhonePairs(null).Should().BeEmpty();
        AuthOptions.ParseTestPhonePairs("").Should().BeEmpty();
        AuthOptions.ParseTestPhonePairs("   ").Should().BeEmpty();
    }

    [Fact]
    public void Parse_SinglePair_Parses()
    {
        var result = AuthOptions.ParseTestPhonePairs("+380991234567=427135");
        result.Should().ContainKey("+380991234567").WhoseValue.Should().Be("427135");
    }

    [Fact]
    public void Parse_MultiplePairs_ParsesAll()
    {
        var result = AuthOptions.ParseTestPhonePairs("+380991234567=427135,+380931112222=900817");
        result.Should().HaveCount(2);
        result["+380931112222"].Should().Be("900817");
    }

    [Fact]
    public void Parse_WhitespaceAroundEntries_IsTrimmed()
    {
        var result = AuthOptions.ParseTestPhonePairs(" +380991234567 = 427135 , +380931112222=900817 ");
        result["+380991234567"].Should().Be("427135");
        result["+380931112222"].Should().Be("900817");
    }

    [Fact]
    public void Parse_EntryWithoutSeparator_IsSkipped()
    {
        // A typo in one optional env var must not stop the API from starting.
        var result = AuthOptions.ParseTestPhonePairs("+380991234567=427135,not-a-pair");
        result.Should().ContainKey("+380991234567");
        result.Should().HaveCount(1);
    }

    [Fact]
    public void Parse_DuplicateNumber_LastWins()
    {
        var result = AuthOptions.ParseTestPhonePairs("+380991234567=111111,+380991234567=222222");
        result["+380991234567"].Should().Be("222222");
    }

    [Fact]
    public void Parse_MalformedCode_IsKeptForResolveCodeToLog()
    {
        // Code validation (six digits) intentionally happens at ResolveCode time,
        // where the number is available for a useful warning log.
        var result = AuthOptions.ParseTestPhonePairs("+380991234567=12345");
        result["+380991234567"].Should().Be("12345");
    }
}

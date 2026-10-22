using FluentAssertions;
using FuelFlow.Features.Settlement;
using Xunit;

namespace FuelFlow.UnitTests.Settlement;

/// <summary>
/// The settlement parser's whole job is to refuse. Every case below is a message somebody could
/// plausibly type, and the assertion is that only one shape of message becomes money.
/// </summary>
public class SettlementMessageParserTests
{
    [Theory]
    [InlineData("1500 K7P2M4XB", 1500.00, "K7P2M4XB")]
    [InlineData("1500.50 K7P2M4XB", 1500.50, "K7P2M4XB")]
    [InlineData("1500,50 K7P2M4XB", 1500.50, "K7P2M4XB")]
    [InlineData("  1500   K7P2M4XB  ", 1500.00, "K7P2M4XB")]
    [InlineData("0.01 K7P2M4XB", 0.01, "K7P2M4XB")]
    // Lower case, because Telegram keyboards autocapitalise and autocorrect. The code is stored upper
    // case, and an operator typing a lower-case code must not silently fail to match.
    [InlineData("1500 k7p2m4xb", 1500.00, "K7P2M4XB")]
    // A code that matches nobody is still a well-formed message: the money arrived and becomes an
    // unallocated payment. The parser checks shape, not meaning.
    [InlineData("1500 ZZZZZZZZ", 1500.00, "ZZZZZZZZ")]
    public void The_one_accepted_shape(string text, decimal expectedAmount, string expectedCode)
    {
        var result = SettlementMessageParser.Parse(text);

        result.Success.Should().BeTrue(result.Rejection);
        result.Amount.Should().Be(expectedAmount);
        result.Code.Should().Be(expectedCode);
    }

    [Theory]
    // The three that a lenient parser would try to be helpful about. Each is the same intent and a
    // different reading, which is exactly how one message becomes two payments.
    [InlineData("1 500 K7P2M4XB", "a space inside the number is a thousands separator, and it is ambiguous")]
    [InlineData("1500грн K7P2M4XB", "a currency word is not part of the format")]
    [InlineData("1500 UAH K7P2M4XB", "a currency word is not part of the format")]
    [InlineData("1500,00.00 K7P2M4XB", "two decimal separators")]
    [InlineData("1500.567 K7P2M4XB", "three decimal places")]
    [InlineData("K7P2M4XB 1500", "the code first and the amount second is a different format")]
    [InlineData("1500 K7P2M4XB K9W8N6QD", "two codes in one message is not one payment")]
    [InlineData("1500 K7P2M4X", "seven characters is not a code")]
    [InlineData("1500 K7P2M4XBA", "nine characters is not a code")]
    [InlineData("1500 K7P2M4-B", "punctuation is not part of a code")]
    [InlineData("перевірка", "prose is not a settlement")]
    [InlineData("", "an empty message")]
    [InlineData("   ", "whitespace is not a settlement")]
    public void Everything_else_is_refused(string text, string why)
    {
        var result = SettlementMessageParser.Parse(text);

        result.Success.Should().BeFalse();
        result.Amount.Should().BeNull();
        result.Code.Should().BeNull();
        // The refusal names the expected shape, because a finance team that agrees on one format is a
        // better outcome than a parser that guesses.
        result.Rejection.Should().NotBeNullOrWhiteSpace(why);
    }

    [Theory]
    [InlineData("0 K7P2M4XB")]
    [InlineData("0.00 K7P2M4XB")]
    public void Zero_is_refused(string text)
    {
        // A zero-amount "payment" is not money. Stated as its own case because the parser accepts a
        // bare `0` as a well-formed integer, and only this check stands between it and the ledger.
        var result = SettlementMessageParser.Parse(text);

        result.Success.Should().BeFalse();
        result.Rejection.Should().Contain("greater than zero");
    }

    [Fact]
    public void A_rejection_always_says_what_was_expected()
    {
        var result = SettlementMessageParser.Parse("1500");

        result.Rejection.Should().Contain("1500 K7P2M4XB")
            .And.Contain("two payments",
                "the refusal should explain why being strict is the safer behaviour, not just that it failed");
    }
}
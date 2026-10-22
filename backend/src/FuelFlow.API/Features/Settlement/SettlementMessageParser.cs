namespace FuelFlow.Features.Settlement;

/// <summary>
/// The one shape a settlement message is allowed to have: an amount, a space, and an eight-character
/// customer code. Everything else is not parsed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Strict on purpose.</b> <c>1500</c>, <c>1 500,00</c> and <c>1500грн</c> are three different
/// problems, and the lenient parser that tries to be helpful to all three is how one payment becomes
/// two — the same message read twice with different interpretations, or read once and half-matched.
/// A message that does not fit becomes no payment and a recorded rejection, which an operator can
/// see; a message parsed "probably right" becomes money that is attributed to someone on a guess.
/// </para>
/// <para>
/// <b>One decimal separator, no thousands separators.</b> Both <c>.</c> and <c>,</c> are accepted as
/// the decimal point because finance will type both, and which one is intended is unambiguous when
/// the group is two digits. A space inside a number is not accepted at all: that is the grouping
/// convention that makes <c>1 500</c> ambiguous against <c>1</c> and <c>500</c>.
/// </para>
/// <para>
/// <b>The code is checked for shape, not for meaning.</b> An eight-character code that matches nobody
/// is still a well-formed message — it becomes an unallocated payment for an operator, which is the
/// correct outcome for money that arrived and cannot be placed. Rejecting it at the parser would throw
/// away the only evidence that the money exists.
/// </para>
/// </remarks>
public static class SettlementMessageParser
{
    private const int CodeLength = 8;

    /// <summary>Upper bound on the whole-figure part, so a paste of a national ID cannot become money.</summary>
    private const int MaxWholeDigits = 9;

    private static readonly System.Text.RegularExpressions.Regex Shape =
        new(
            @"^\s*(?<whole>\d{1," + MaxWholeDigits + @"})(?:[.,](?<frac>\d{1,2}))?\s+(?<code>[A-Za-z0-9]{" + CodeLength + @"})\s*$",
            System.Text.RegularExpressions.RegexOptions.Compiled |
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Reads the message, or explains why it cannot be read. The failure text names the expected
    /// shape: a finance team writing in one agreed format is a far better outcome than a parser that
    /// guesses, and a message that fails should say what would have worked.
    /// </summary>
    public static SettlementMessageParse Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return SettlementMessageParse.NotParsed("The message was empty.");
        }

        var match = Shape.Match(text);
        if (!match.Success)
        {
            return SettlementMessageParse.NotParsed(
                $"Expected an amount, a space, and an {CodeLength}-character customer code — for example " +
                $"`1500 K7P2M4XB`. Thousands separators, a currency word, or a second code are not accepted, " +
                $"because a message that can be read two ways becomes two payments.");
        }

        var whole = match.Groups["whole"].Value;
        var fraction = match.Groups["frac"].Success ? match.Groups["frac"].Value : null;
        var code = match.Groups["code"].Value.ToUpperInvariant();

        if (!decimal.TryParse(
                fraction is null ? whole : whole + "." + fraction,
                // AllowDecimalPoint and nothing else. The shape above has already limited the input to
                // digits with at most one separator and two decimals, so the alternative styles -
                // thousands separators, signs, exponents, currency - are not shapes this can reach.
                // NumberStyles.None would refuse "1500.50" outright, which is a format finance will type.
                System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture,
                out var amount))
        {
            // Unreachable given the regex, and asserted rather than assumed: a silent zero here would
            // write a payment of 0.00 UAH into the ledger. The text is NOT quoted back: it carries the
            // customer's settlement code, the row stores it in its own column, and this reason is
            // written to the log as well.
            return SettlementMessageParse.NotParsed("The amount could not be read.");
        }

        if (amount <= 0m)
        {
            return SettlementMessageParse.NotParsed("A settlement amount must be greater than zero.");
        }

        return SettlementMessageParse.Parsed(amount, code);
    }
}

/// <summary>Either the two values, or the reason there are none. Never both.</summary>
public sealed record SettlementMessageParse(bool Success, decimal? Amount, string? Code, string? Rejection)
{
    public static SettlementMessageParse Parsed(decimal amount, string code) =>
        new(true, amount, code, null);

    public static SettlementMessageParse NotParsed(string rejection) =>
        new(false, null, null, rejection);
}
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FuelFlow.UnitTests.Architecture;

/// <summary>
/// Architecture guard for the vulnerability-disclosure file (RFC 9116).
///
/// <para>
/// F-01 in the WSTG checklist: no host served <c>/.well-known/security.txt</c>, so someone who
/// found a problem had no address to report it to - on the marketing site a <c>404</c>, on the
/// panel the SPA's <c>index.html</c>, on the API the auth fallback's <c>401</c>. The file now comes
/// from one snippet that every production host imports, and this guard keeps it that way.
/// </para>
/// <para>
/// The second test is deliberately time-dependent: <c>Expires</c> is mandatory under the RFC, and a
/// file whose expiry has passed is worse than none because it tells a researcher not to bother.
/// When it fails, renew the date in <c>deploy/Caddyfile</c> - that is the reminder working.
/// </para>
/// </summary>
public sealed class SecurityTxtGuardTests
{
    private static readonly string[] ProductionHosts =
    [
        "{$ROOT_DOMAIN}",
        "{$API_DOMAIN}",
        "palne.shop, www.palne.shop"
    ];

    [Fact]
    public void Every_Production_Host_Should_Serve_The_Disclosure_File()
    {
        var blocks = Blocks(Caddyfile()).ToDictionary(b => b.Address, b => b.Body, StringComparer.Ordinal);

        blocks.Should().ContainKey("(security_txt)",
            "the file is defined once, as a snippet, so the four origins cannot drift");

        blocks["(security_txt)"].Should()
            .Contain("respond /.well-known/security.txt",
                "the snippet must answer the RFC's well-known path")
            .And.Contain("Contact:", "a disclosure file with no contact is not a disclosure file");

        var silent = ProductionHosts
            .Where(host => !blocks.TryGetValue(host, out var body)
                        || !body.Contains("import security_txt", StringComparison.Ordinal))
            .ToArray();

        silent.Should().BeEmpty(
            "every public host has to serve the file; the one that does not is where a reporter gives up");
    }

    [Fact]
    public void The_Expiry_Should_Be_In_The_Future_And_Within_A_Year()
    {
        var body = Blocks(Caddyfile())
            .Single(b => b.Address == "(security_txt)")
            .Body;

        var match = Regex.Match(body, @"^Expires:\s*(?<value>\S+)\s*$", RegexOptions.Multiline);
        match.Success.Should().BeTrue("RFC 9116 requires an Expires field");

        var expires = DateTimeOffset.Parse(
            match.Groups["value"].Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        var now = TimeProvider.System.GetUtcNow();

        expires.Should().BeAfter(now,
            "an expired security.txt tells a researcher the address is dead");

        expires.Should().BeBefore(now.AddYears(1).AddDays(1),
            "RFC 9116 asks for less than a year, so the address is re-confirmed rather than assumed");
    }

    private static string Caddyfile()
        => File.ReadAllText(Path.Combine(RepositoryRoot(), "deploy", "Caddyfile"));

    /// <summary>
    /// The site blocks and snippets of a Caddyfile, keyed by their address line.
    /// </summary>
    /// <remarks>
    /// A block starts on an unindented line that ends with <c>{</c> and closes on an unindented
    /// <c>}</c>; Caddyfile syntax does not nest site blocks, so a brace counter is not needed.
    /// </remarks>
    private static IReadOnlyList<(string Address, string Body)> Blocks(string caddyfile)
    {
        var lines = caddyfile.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var blocks = new List<(string Address, string Body)>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0 || char.IsWhiteSpace(line[0]) || !line.EndsWith('{'))
                continue;

            var body = new StringBuilder();
            var end = i + 1;
            for (; end < lines.Length && lines[end] != "}"; end++)
                body.AppendLine(lines[end]);

            blocks.Add((line.TrimEnd('{').Trim(), body.ToString()));
            i = end;
        }

        return blocks;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "deploy", "Caddyfile"))
                && Directory.Exists(Path.Combine(directory.FullName, "backend", "src")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repository root (a directory holding deploy/Caddyfile and backend/src) " +
            $"walking up from {AppContext.BaseDirectory}.");
    }
}

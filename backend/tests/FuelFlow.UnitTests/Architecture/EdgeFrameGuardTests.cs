using System.Text;
using FluentAssertions;

namespace FuelFlow.UnitTests.Architecture;

/// <summary>
/// Architecture guard for clickjacking protection at the edge.
///
/// <para>
/// Three public hosts, three chances to forget the header. The panel and the API take it from the
/// shared <c>security_headers</c> snippet; the marketing site keeps its own lighter header block
/// because the admin CSP would break Next.js, and that block silently dropped the frame refusal
/// (F-02 in the WSTG checklist). Nothing failed, because a missing header is a valid response.
/// </para>
/// <para>
/// The guard resolves the snippet one level: a site block satisfies it either by naming
/// <c>X-Frame-Options</c> itself or by importing <c>security_headers</c>, whose body then has to
/// carry it. That way removing the header from the snippet fails here as well as removing it from
/// the marketing block.
/// </para>
/// </summary>
public sealed class EdgeFrameGuardTests
{
    [Fact]
    public void Every_Production_Host_Should_Refuse_Framing()
    {
        var caddyfile = File.ReadAllText(Path.Combine(RepositoryRoot(), "deploy", "Caddyfile"));
        var blocks = Blocks(caddyfile);

        var snippetSetsIt = blocks
            .Any(b => b.Address == "(security_headers)" && b.Body.Contains("X-Frame-Options", StringComparison.Ordinal));

        // Guard against a vacuous pass: if the parser stops finding site blocks, every
        // host below disappears instead of failing.
        var production = blocks
            .Where(b => b.Address.Contains("{$ROOT_DOMAIN}", StringComparison.Ordinal)
                     || b.Address.Contains("{$API_DOMAIN}", StringComparison.Ordinal)
                     || b.Address.Contains("palne.shop, www.palne.shop", StringComparison.Ordinal))
            .ToList();

        production.Should().HaveCount(3,
            "the Caddyfile should define exactly the three production hosts this guard knows about");

        var unframed = production
            .Where(b => !b.Body.Contains("X-Frame-Options", StringComparison.Ordinal)
                     && !(snippetSetsIt && b.Body.Contains("import security_headers", StringComparison.Ordinal)))
            .Select(b => b.Address)
            .ToArray();

        unframed.Should().BeEmpty(
            "every public host must refuse a frame; without it any page here can be embedded and clicked through");
    }

    /// <summary>
    /// The site blocks and snippets of a Caddyfile, keyed by their address line.
    /// </summary>
    /// <remarks>
    /// A block starts on an unindented line that ends with <c>{</c> and closes on an unindented
    /// <c>}</c>. Caddyfile syntax does not nest site blocks, so a brace counter is not needed; the
    /// first unindented closing brace ends the block.
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

using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The outbox's two halves must agree: an event type that is CONSUMED has to be PRODUCED
/// somewhere, and an event type that is PRODUCED has to be either consumed or explicitly
/// recorded-only.
/// <para>
/// The codebase already states the second half in a comment, in
/// <c>BulkActionVouchersCommandHandler</c>, about why it deliberately does not write a
/// <c>VoucherActivated</c> row: "nothing consumes that type, so it only left permanently
/// unprocessed rows that inflate the admin 'unprocessed outbox events' alert". Writing a row
/// nobody ever processes is a leak, and the comment says so.
/// </para>
/// <para>
/// The first half is the one that was broken. <c>FulfillmentService.ProcessVoucherImportsAsync</c>
/// consumed <c>VoucherExpired</c> outbox rows - and nothing in the codebase has ever written
/// one. It was never called either. So it could not do anything at runtime, while reading like
/// the code that handles voucher expiry. A reader would reasonably conclude that expiry is
/// handled there. It is not: the real mechanism is
/// <c>ExpiredVoucherLossService</c>, which flips a voucher's status and is scheduled
/// separately.
/// </para>
/// <para>
/// A consumer without a producer is the harder of the two to spot, because it is invisible at
/// runtime - there is nothing to fail. It only shows up as a reader's wrong assumption, which
/// is exactly how it survived.
/// </para>
/// </summary>
public sealed class OutboxEventTypeContractTests
{
    private static readonly Regex EventTypeReference = new(@"OutboxEventType\.(\w+)", RegexOptions.Compiled);

    [Fact]
    public void Every_Consumed_Outbox_Event_Type_Is_Also_Produced()
    {
        var (consumed, produced) = Scan();

        consumed.Should().NotBeEmpty("the scan found no consumers at all, so it is not looking at the right code");
        produced.Should().NotBeEmpty("the scan found no producers at all, so it is not looking at the right code");

        // The assertion names the types that break the contract, so a regression points at one
        // event type rather than at "something is wrong with the outbox".
        var orphans = consumed.Except(produced).OrderBy(x => x).ToArray();

        orphans.Should().BeEmpty(
            "these outbox event types are consumed but never produced: {0}. A consumer with no "
            + "producer can never run, so it is dead code that reads like a working handler. "
            + "Either produce the event, or delete the consumer.",
            string.Join(", ", orphans));
    }

    /// <summary>
    /// The other direction - every produced type is consumed - is deliberately NOT asserted.
    /// A recorded-only type is legitimate: <c>VoucherActivated</c> is written for the audit
    /// trail and is never processed, and <c>BulkActionVouchersCommandHandler</c> says so
    /// explicitly. Which types are meant to be record-only is a decision, not an invariant this
    /// test can make.
    /// </summary>
    private static (HashSet<string> Consumed, HashSet<string> Produced) Scan()
    {
        var consumed = new HashSet<string>(StringComparer.Ordinal);
        var produced = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in SourceFiles())
        {
            foreach (var line in StripComments(File.ReadAllText(file)).Split('\n'))
            {
                foreach (Match match in EventTypeReference.Matches(line))
                {
                    var name = match.Groups[1].Value;

                    // A comparison reads the type; anything else writes it. The two shapes are
                    // `EventType == OutboxEventType.X` and `EventType = OutboxEventType.X`, and
                    // a `switch` case reads it too.
                    var before = line[..match.Index];
                    var isRead = before.Contains("==", StringComparison.Ordinal)
                                 || before.TrimEnd().EndsWith("case", StringComparison.Ordinal)
                                 || before.TrimEnd().EndsWith("case ", StringComparison.Ordinal);

                    if (isRead)
                    {
                        consumed.Add(name);
                    }
                    else
                    {
                        produced.Add(name);
                    }
                }
            }
        }

        return (consumed, produced);
    }

    private static IEnumerable<string> SourceFiles()
    {
        var apiProject = Path.Combine(FindProjectRoot(), "src", "FuelFlow.API");

        return Directory
            .EnumerateFiles(apiProject, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();
    }

    /// <summary>
    /// Comments are removed because they are where this codebase documents its intentions -
    /// and a comment that names an event type is not a producer of it.
    /// </summary>
    private static string StripComments(string source)
    {
        source = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        return Regex.Replace(source, @"//.*?$", " ", RegexOptions.Multiline);
    }

    private static string FindProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "FuelFlow.slnx")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("could not locate the backend project root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}

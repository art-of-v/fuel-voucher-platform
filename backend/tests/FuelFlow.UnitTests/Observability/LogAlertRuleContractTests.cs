using System.Text.RegularExpressions;
using FluentAssertions;

namespace FuelFlow.UnitTests.Observability;

/// <summary>
/// Guards the contract between the log messages the application emits and the Loki alert rules that
/// are supposed to notice them.
/// <para>
/// The Prometheus rules have a contract test (<see cref="MetricNameContractTests"/>), because the
/// OpenTelemetry exporter rewrites metric names. The Loki rules had none, and that gap is exactly
/// how the #246 concurrency detector ended up invisible: <c>ApplicationDbContext</c> logs every
/// <c>DbUpdateConcurrencyException</c> at <b>Warning</b>, the only log-based alert filtered
/// <c>level=~"error|fatal"</c>, and so every collision was written to a log line that no alert and
/// no human would ever read. The detector fired correctly and told nobody.
/// </para>
/// <para>
/// That failure mode is silent in the worst way - a rule that matches nothing looks identical to a
/// healthy system - so these tests bind the rules to the strings and levels in the code rather than
/// trusting that someone remembers to update both ends.
/// </para>
/// </summary>
public class LogAlertRuleContractTests
{
    private static string RulesPath() => Path.Combine(
        RepositoryRoot(),
        "backend", "observability", "grafana", "provisioning", "alerting-telegram", "alert-rules.yml");

    private static string DbContextPath() => Path.Combine(
        RepositoryRoot(), "backend", "src", "FuelFlow.API", "Persistence", "ApplicationDbContext.cs");

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        // In a normal clone .git is a directory; in a git worktree it is a FILE (a gitdir pointer),
        // so accept either or the walk runs off the top of the drive.
        while (dir != null &&
               !Directory.Exists(Path.Combine(dir.FullName, ".git")) &&
               !File.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the tests must run from inside the repository");
        return dir!.FullName;
    }

    private static string ReadFile(string path)
    {
        File.Exists(path).Should().BeTrue($"expected to find {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void ConcurrencyDetector_IsAlertedOn_NotJustLogged()
    {
        var rules = ReadFile(RulesPath());

        // The detector's own log line, verbatim. If someone rewords the message and not the rule,
        // this fails - which is the point, because a reworded message that no longer matches an
        // alert is indistinguishable from "no conflicts are happening".
        rules.Should().Contain("Concurrency conflict on",
            "the #246 detector logs \"Concurrency conflict on {Entity} ...\" and a rule has to match that string");

        // And the rule must select warning, not error: the detector is deliberately Warning so a
        // routine collision cannot trip the operator pager, which is also why LogErrorBurst does
        // not see it.
        rules.Should().Contain("level=\"warning\"",
            "the concurrency detector logs at Warning; an alert filtering error|fatal will never see it");

        rules.Should().Contain("fuelflow-concurrency-conflict",
            "the #246 detector needs an alert rule uid of its own");
    }

    [Fact]
    public void ConcurrencyDetector_LogsAtWarning_SoTheRuleActuallyMatches()
    {
        var context = ReadFile(DbContextPath());

        context.Should().Contain("Concurrency conflict on",
            "the rule matches this message, so the detector has to actually emit it");

        // Pin the level rather than trusting the review above to stay accurate. If someone raises
        // this to LogError to make the alert fire, that is a real behavioural change (it would
        // start paging) and it should be a deliberate one, caught here.
        context.Should().Contain("LogWarning",
            "the detector logs at Warning; raising it to Error would page the operator on every collision");
    }

    [Fact]
    public void EveryLokiRule_HasAUniqueUid()
    {
        // Grafana silently ignores a duplicate uid, so two rules can collapse into one without any
        // error anywhere.
        var uids = Regex.Matches(ReadFile(RulesPath()), @"uid:\s*([a-z0-9-]+)")
            .Select(m => m.Groups[1].Value)
            .ToList();

        uids.Should().NotBeEmpty("the alert rules file is expected to declare uids");
        uids.Should().OnlyHaveUniqueItems("Grafana silently drops a duplicate uid, so two rules would collapse into one");
    }

    /// <summary>
    /// A price move has to leave a line the operator can find, and production logs at Information.
    /// <para>
    /// Measured on 2026-10-27: UPG's gas and three WOG grades had moved since the log window opened,
    /// and neither sync had written anything - the summary was Debug. OKKO and KLO log the same
    /// event at Information, so the only search that answered "when did this move?" was
    /// <c>fuel_price_history</c>. Nothing failed and nothing alerted; the record simply was not there.
    /// </para>
    /// </summary>
    [Fact]
    public void UpgWogRepricing_IsLoggedAtInformation()
    {
        var source = ReadFile(Path.Combine(
            RepositoryRoot(), "backend", "src", "FuelFlow.API", "BackgroundJobs", "UpgWogPriceSyncService.cs"));

        source.Should().MatchRegex(
            @"LogInformation\(\s*""\{Brand\} sync repriced",
            "production's minimum level is Information, so the repricing summary has to be written at Information");

        source.Should().NotMatchRegex(
            @"LogDebug\(\s*""\{Brand\} sync repriced",
            "a repricing summary at Debug reaches no log, no search and no alert");
    }

    [Fact]
    public void EveryLokiRule_DeclaresATitle()
    {
        var uids = Regex.Matches(ReadFile(RulesPath()), @"uid:\s*([a-z0-9-]+)")
            .Select(m => m.Groups[1].Value)
            .ToList();

        var titles = Regex.Matches(ReadFile(RulesPath()), @"title:\s*(\S+)")
            .Select(m => m.Groups[1].Value)
            .ToList();

        titles.Count.Should().Be(uids.Count,
            "every rule needs a title for the Telegram notification; a mismatch means one was added without the other");
    }
}
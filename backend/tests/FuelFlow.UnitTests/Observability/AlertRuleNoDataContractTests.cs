using System.Text.RegularExpressions;
using FluentAssertions;

namespace FuelFlow.UnitTests.Observability;

/// <summary>
/// Guards the other half of the alert-rule contract: that a rule whose healthy value is zero can
/// actually say so.
/// <para>
/// <c>LogAlertRuleContractTests</c> binds a rule to the log line it matches, and
/// <c>MetricNameContractTests</c> binds a rule to the metric that exists. Neither can see the
/// failure measured on 2026-10-27: nine of the thirteen rules had been sitting in <c>NoData</c>
/// since the observability volumes were recreated, and Telegram was getting a notification every
/// four hours. The queries were correct; they simply had nothing to return. A counter that has
/// never been incremented has no series, a gauge fed no rows emits nothing, and a Loki query over
/// zero matching lines returns nothing - so a system behaving perfectly is indistinguishable from
/// an exporter that stopped reporting.
/// </para>
/// <para>
/// The fix is per datasource: PromQL rules carry <c>or vector(0)</c>, because 0 is the answer the
/// healthy system should have given; Loki rules carry <c>noDataState: OK</c>, because "no error
/// lines" is the healthy state itself. These tests keep a later edit from quietly dropping either.
/// </para>
/// </summary>
public class AlertRuleNoDataContractTests
{
    private static string RulesPath() => Path.Combine(
        RepositoryRoot(),
        "backend", "observability", "grafana", "provisioning", "alerting-telegram", "alert-rules.yml");

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        // In a git worktree .git is a file rather than a directory; accept either or the walk runs
        // off the top of the drive.
        while (dir != null &&
               !Directory.Exists(Path.Combine(dir.FullName, ".git")) &&
               !File.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the tests must run from inside the repository");
        return dir!.FullName;
    }

    /// <summary>One block per rule, keyed by uid, up to the next rule.</summary>
    private static Dictionary<string, string> RuleBlocks()
    {
        var text = File.ReadAllText(RulesPath());
        var blocks = Regex.Split(text, @"(?m)^      - uid: ").Skip(1);

        return blocks.ToDictionary(
            block => block[..block.IndexOf('\n')].Trim(),
            block => block);
    }

    [Theory]
    [InlineData("fuelflow-high-error-rate")]
    [InlineData("fuelflow-voucher-pool-low")]
    [InlineData("fuelflow-fulfillment-failures")]
    [InlineData("fuelflow-job-failures")]
    [InlineData("fuelflow-payments-stuck")]
    [InlineData("fuelflow-refresh-reuse-spike")]
    public void ZeroIsHealthyRule_ReportsZeroInsteadOfGoingNoData(string uid)
    {
        RuleBlocks().Should().ContainKey(uid);

        RuleBlocks()[uid].Should().Contain("or vector(0)",
            $"'{uid}' thresholds a value whose healthy state is 0, and PromQL returns no series for a " +
            "counter or label combination that has never been observed - so without the fallback the " +
            "rule can never be Normal, only NoData");
    }

    [Theory]
    [InlineData("fuelflow-log-error-burst")]
    [InlineData("fuelflow-log-fatal")]
    [InlineData("fuelflow-concurrency-conflict")]
    [InlineData("fuelflow-job-failed")]
    public void LogRule_TreatsNoMatchingLinesAsOkay(string uid)
    {
        RuleBlocks().Should().ContainKey(uid);

        RuleBlocks()[uid].Should().Contain("noDataState: OK",
            $"'{uid}' counts log lines, so an empty result is the healthy state and must not notify");
    }

    [Fact]
    public void NoLokiRule_NotifiesOnNoData()
    {
        var offenders = RuleBlocks()
            .Where(rule => rule.Value.Contains("datasourceUid: fuelflow-loki"))
            .Where(rule => rule.Value.Contains("noDataState: NoData"))
            .Select(rule => rule.Key)
            .ToList();

        offenders.Should().BeEmpty(
            "a Loki rule returning no lines is reporting a healthy log, not a missing datasource; " +
            "NoData there is a notification every repeat_interval for doing nothing wrong");
    }
}

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
/// The fix is per datasource: PromQL rules carry a fallback to 0, because 0 is the answer the
/// healthy system should have given; Loki rules carry <c>noDataState: OK</c>, because "no error
/// lines" is the healthy state itself. These tests keep a later edit from quietly dropping either.
/// </para>
/// <para>
/// The PromQL fallback is <c>or vector(0)</c> only where the label set matches: a query that
/// aggregates its labels away (<c>sum(...)</c>, <c>absent(...)</c>). Added to a labelled series it
/// is a second, untagged instance - and under a <c>lt</c> threshold it is a permanent firing alert
/// with no labels (planning #778). Such a rule gets a second rule for the missing-metric case
/// instead; <c>AnUntaggedZeroFallback_IsNeverThresholdedFromBelow</c> is what holds that line.
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

    /// <summary>
    /// The block without its YAML comments. A block runs up to the next rule, so the comment that
    /// introduces that rule lands in the previous block - and a comment explaining why a construct
    /// is absent must not read as the construct being present.
    /// </summary>
    private static string WithoutComments(string block) =>
        string.Join('\n', block.Split('\n').Where(line => !line.TrimStart().StartsWith('#')));

    [Theory]
    [InlineData("fuelflow-high-error-rate")]
    [InlineData("fuelflow-voucher-pool-metric-absent")]
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

    [Fact]
    public void AnUntaggedZeroFallback_IsNeverThresholdedFromBelow()
    {
        // `or` matches by label set. `vector(0)` is the series `{}`, and every series of a labelled
        // query carries its own labels, so the fallback is added as a separate instance whose value
        // is 0 - below any `lt` threshold. That instance fires for ever, with no brand and no fuel
        // type, next to the real stock-out the rule is supposed to report (planning #778).
        var offenders = RuleBlocks()
            .Where(rule => WithoutComments(rule.Value).Contains("or vector(0)"))
            .Where(rule => WithoutComments(rule.Value).Contains("type: lt"))
            .Select(rule => rule.Key)
            .ToList();

        offenders.Should().BeEmpty(
            "an untagged zero is a real series, not an absence; under a lower-than threshold it is a " +
            "permanent firing instance with empty labels. Report a missing metric with its own " +
            "absent() rule instead");
    }

    [Fact]
    public void ThePoolRule_LeavesAbsenceToItsOwnAlert()
    {
        var rule = WithoutComments(RuleBlocks()["fuelflow-voucher-pool-low"]);

        rule.Should().Contain("expr: fuelflow_vouchers_pool_available_vouchers");
        rule.Should().NotContain("or vector(0)",
            "the gauge is labelled per brand and fuel type, so an untagged fallback would be a " +
            "permanent firing instance (planning #778)");
        rule.Should().Contain("noDataState: OK",
            "a missing gauge is reported by fuelflow-voucher-pool-metric-absent; leaving NoData here " +
            "would notify twice for one missing metric");
        RuleBlocks().Should().ContainKey("fuelflow-voucher-pool-metric-absent");
    }

    [Fact]
    public void ThePoolRule_NamesThePackSizeItIsAbout()
    {
        // Fulfilment matches a line on brand, fuel type and litres, so a brand/fuel total that
        // reads healthy can still have nothing for the size an order asks for - the shape planning
        // #781 was filed on. One instance per size without the size in the message is the same
        // sentence twice for two different things to buy.
        var rule = WithoutComments(RuleBlocks()["fuelflow-voucher-pool-low"]);

        rule.Should().Contain("$labels.liters",
            "the pack size is half of what makes a pool a pool, and the operator has to know which one to top up");
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

    /// <summary>
    /// The stale-price rule has to name the brand it is about, because the brand is the only thing
    /// the operator can act on - four feeds share the pipeline, and "prices are stale" without a name
    /// sends them to read four jobs' logs.
    /// </summary>
    [Fact]
    public void TheStalePriceRule_NamesTheBrandAndDoesNotTreatNoDataAsHealthy()
    {
        RuleBlocks().Should().ContainKey("fuelflow-price-sync-stale");

        var rule = RuleBlocks()["fuelflow-price-sync-stale"];

        rule.Should().Contain("$labels.brand",
            "the gauge carries one series per brand, and the notification has to say which one stopped");
        rule.Should().Contain("noDataState: Alerting",
            "for a freshness check, no data means the age cannot be proven - which is not a healthy " +
            "state, and is exactly the nineteen hours of silence issue #777 exists to end");
        rule.Should().NotContain("or vector(0)",
            "a zero fallback would report 'fresh' whenever the gauge could not be read");
    }
}

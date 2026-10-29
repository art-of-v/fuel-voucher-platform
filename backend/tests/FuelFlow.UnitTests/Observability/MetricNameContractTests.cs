using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using FuelFlow.SharedKernel.Observability;

namespace FuelFlow.UnitTests.Observability;

/// <summary>
/// Guards the contract between the instruments declared in <see cref="FuelFlowMetrics"/> and the
/// metric names referenced by the Prometheus alert rules and the Grafana dashboards.
/// <para>
/// This exists because of a real outage-class bug: the OpenTelemetry Prometheus exporter injects
/// the instrument's <em>unit</em> into the exported name (so <c>fuelflow.jobs.failed</c> with unit
/// <c>jobs</c> is exported as <c>fuelflow_jobs_failed_jobs_total</c>, not
/// <c>fuelflow_jobs_failed_total</c>). Alert rules written against the intuitive name match nothing
/// and stay silent forever, which looks identical to "everything is healthy". Renaming an
/// instrument or merely changing its unit is enough to silently break an alert, and nothing else in
/// the build would notice.
/// </para>
/// </summary>
public class MetricNameContractTests
{
    /// <summary>
    /// Units are emitted in their expanded form (UCUM abbreviations are spelled out). Only the
    /// units actually used by <see cref="FuelFlowMetrics"/> need to be listed.
    /// </summary>
    private static readonly Dictionary<string, string> UnitSuffixes = new()
    {
        ["ms"] = "milliseconds",
        ["s"] = "seconds"
    };

    /// <summary>
    /// Reproduces the exporter's naming: dots become underscores, the unit is appended, and
    /// counters (monotonic sums) additionally get <c>_total</c>. Verified against a live scrape of
    /// both the API and the JobsWorker.
    /// </summary>
    private static string ToPrometheusName(string instrumentName, string? unit, bool isCounter)
    {
        var name = instrumentName.Replace('.', '_');

        if (!string.IsNullOrEmpty(unit))
        {
            var suffix = UnitSuffixes.TryGetValue(unit, out var mapped) ? mapped : unit;
            name = $"{name}_{suffix}";
        }

        return isCounter ? $"{name}_total" : name;
    }

    /// <summary>
    /// Metric families that do not come from <see cref="FuelFlowMetrics"/> but are scraped from the
    /// same endpoints: the OpenTelemetry runtime and ASP.NET Core instrumentation, plus Prometheus'
    /// own <c>up</c>. Kept as an explicit allowlist rather than a wildcard so that a dashboard
    /// querying a family nobody emits - the original bug here was
    /// <c>process_runtime_dotnet_gc_collections_count_total</c>, which never existed - fails the
    /// test instead of rendering an empty panel.
    /// </summary>
    private static readonly HashSet<string> RuntimeMetricFamilies = new(StringComparer.Ordinal)
    {
        "up",
        "http_server_request_duration_seconds",
        "db_client_operation_duration_seconds",
        "process_memory_usage_bytes",
        "dotnet_gc_collections_total",
        "dotnet_gc_last_collection_heap_size_bytes",
        "kestrel_active_connections",
        "kestrel_queued_connections"
    };

    /// <summary>
    /// Everything that looks like an identifier in PromQL but is not a metric: aggregators,
    /// modifiers, functions, and Grafana's <c>label_values</c>, which appears in templating
    /// queries in the same shape as a function call.
    /// </summary>
    private static readonly HashSet<string> PromQlWords = new(StringComparer.Ordinal)
    {
        "sum", "avg", "min", "max", "count", "count_values", "stddev", "stdvar", "bottomk",
        "topk", "quantile", "by", "without", "on", "ignoring", "group_left", "group_right",
        "bool", "offset", "and", "or", "unless",
        "rate", "irate", "increase", "delta", "idelta", "deriv", "predict_linear", "resets",
        "changes", "abs", "ceil", "floor", "round", "clamp", "clamp_min", "clamp_max", "exp",
        "ln", "log2", "log10", "sqrt", "sgn", "vector", "scalar", "time", "timestamp", "sort",
        "sort_desc", "sort_by_label", "histogram_quantile", "label_replace", "label_join",
        "absent", "absent_over_time", "avg_over_time", "min_over_time", "max_over_time",
        "sum_over_time", "count_over_time", "last_over_time", "present_over_time",
        "quantile_over_time", "stddev_over_time", "stdvar_over_time", "day_of_month",
        "day_of_week", "days_in_month", "hour", "minute", "month", "year",
        "label_values", "inf", "nan"
    };

    /// <summary>
    /// Collects every instrument published by <see cref="FuelFlowMetrics"/>, including the
    /// observable gauges, which are only created when their registration method is called.
    /// </summary>
    private static IReadOnlyList<string> GetExportedMetricNames() =>
        CollectInstrumentNames(instrument =>
                [ToPrometheusName(instrument.Name, instrument.Unit, IsCounter(instrument))])
            .Order()
            .ToList();

    /// <summary>
    /// The same instruments, expressed as the series names a scrape actually produces: histograms
    /// are exported as <c>_bucket</c>/<c>_sum</c>/<c>_count</c> rather than under their own name.
    /// This is what a PromQL query has to name.
    /// </summary>
    private static IReadOnlySet<string> GetExportedSeriesNames() =>
        CollectInstrumentNames(instrument =>
        {
            var name = ToPrometheusName(instrument.Name, instrument.Unit, IsCounter(instrument));
            return instrument.GetType().GetGenericTypeDefinition() == typeof(Histogram<>)
                ? [name, $"{name}_bucket", $"{name}_sum", $"{name}_count"]
                : [name];
        });

    private static IReadOnlySet<string> CollectInstrumentNames(Func<Instrument, IEnumerable<string>> namesOf)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name != ObservabilityConstants.MeterName) return;

            foreach (var name in namesOf(instrument))
            {
                names.Add(name);
            }

            l.EnableMeasurementEvents(instrument);
        };
        listener.Start();

        using var metrics = new FuelFlowMetrics();
        metrics.RegisterVoucherPoolGauge(() => []);
        metrics.RegisterOrderStatusGauge(() => []);
        metrics.RegisterStuckPaymentGauge(() => []);
        metrics.RegisterPriceFreshnessGauge(() => []);

        return names;
    }

    private static bool IsCounter(Instrument instrument) =>
        instrument.GetType().GetGenericTypeDefinition() == typeof(Counter<>);

    /// <summary>
    /// Reads the Grafana rules, not a copy of them. Prometheus used to carry a second set in
    /// observability/prometheus/rules/; nothing delivered those (no Alertmanager is configured),
    /// so this test guarded the copy that could not notify while the rules that do notify were
    /// unchecked. The file is gone and the guard points at the only source.
    /// </summary>
    [Fact]
    public void EveryFuelFlowMetricReferencedByAlertRules_ExistsAsAnInstrument()
    {
        var rulesPath = Path.Combine(
            RepositoryRoot(), "backend", "observability", "grafana", "provisioning", "alerting-telegram", "alert-rules.yml");
        File.Exists(rulesPath).Should().BeTrue($"alert rules were expected at {rulesPath}");

        var exported = GetExportedMetricNames();

        // Bare metric selectors only: skip histogram-derived suffixes, which are generated by the
        // exporter rather than declared as instruments.
        var referenced = Regex.Matches(File.ReadAllText(rulesPath), @"\bfuelflow_[a-z0-9_]+\b")
            .Select(m => m.Value)
            .Select(n => Regex.Replace(n, "_(bucket|sum|count)$", string.Empty))
            .Distinct()
            .ToList();

        referenced.Should().NotBeEmpty("the alert rules are expected to reference FuelFlow metrics");

        foreach (var name in referenced)
        {
            exported.Should().Contain(name,
                $"alert rules query '{name}', but no instrument in FuelFlowMetrics exports that name. " +
                $"Remember the exporter injects the unit into the name. Exported: {string.Join(", ", exported.Order())}");
        }
    }

    /// <summary>
    /// Every metric named by a dashboard panel has to exist, because a dashboard that queries a
    /// name nobody exports renders an empty panel - and an empty panel reads as "no traffic", not
    /// as "the query is wrong". The two GC panels were querying
    /// <c>process_runtime_dotnet_gc_collections_count_total</c> and
    /// <c>process_runtime_dotnet_gc_heap_size_bytes</c>, neither of which is ever exported, and
    /// they had been blank since the day they were written.
    /// </summary>
    [Fact]
    public void EveryMetricQueryInTheDashboards_NamesAMetricThatExists()
    {
        var dashboardDirectory = Path.Combine(
            RepositoryRoot(), "backend", "observability", "grafana", "provisioning", "dashboards");
        var dashboards = Directory.GetFiles(dashboardDirectory, "*.json");
        dashboards.Should().NotBeEmpty($"dashboards were expected under {dashboardDirectory}");

        var known = new HashSet<string>(GetExportedSeriesNames(), StringComparer.Ordinal);
        foreach (var family in RuntimeMetricFamilies)
        {
            known.Add(family);
            known.Add($"{family}_bucket");
            known.Add($"{family}_sum");
            known.Add($"{family}_count");
        }

        var queried = new List<(string Dashboard, string Metric)>();
        var queryCount = 0;

        foreach (var path in dashboards)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var expression in PrometheusQueries(document.RootElement))
            {
                queryCount++;
                queried.AddRange(MetricNamesIn(expression).Select(metric => (Path.GetFileName(path), metric)));
            }
        }

        queryCount.Should().BeGreaterThan(10, "the dashboards are expected to carry Prometheus queries");

        // Guards the guard: if the extractor stops finding anything, the assertion below would pass
        // on an empty list forever.
        var names = queried.Select(q => q.Metric).ToHashSet(StringComparer.Ordinal);
        names.Should().Contain(
            ["up", "http_server_request_duration_seconds_count", "fuelflow_vouchers_pool_available_vouchers"],
            "the scan must see the panels it is meant to check");

        var unknown = queried
            .Where(q => !known.Contains(q.Metric))
            .Distinct()
            .ToList();
        unknown.Should().BeEmpty(
            "a dashboard querying a metric that is never exported renders a permanently empty panel. " +
            "Unknown: " + string.Join(", ", unknown.Select(q => $"{q.Metric} ({q.Dashboard})")));
    }

    /// <summary>
    /// Pulls the PromQL out of a dashboard: panel targets and templating queries. Panels are
    /// selected by their datasource, so the Logs dashboard (LogQL, labels the same way) is skipped
    /// rather than tokenised as if its stream selectors were metric names.
    /// </summary>
    private static IEnumerable<string> PrometheusQueries(JsonElement dashboard)
    {
        foreach (var panel in Panels(dashboard))
        {
            var panelDatasource = PrometheusDatasource(panel);

            foreach (var target in Children(panel, "targets"))
            {
                if (PrometheusDatasource(target) ?? panelDatasource ?? false)
                {
                    if (target.TryGetProperty("expr", out var expr) && expr.ValueKind == JsonValueKind.String)
                    {
                        yield return expr.GetString()!;
                    }
                }
            }
        }

        foreach (var variable in Children(dashboard, "templating").SelectMany(t => Children(t, "list")))
        {
            if (PrometheusDatasource(variable) != true ||
                !variable.TryGetProperty("query", out var query))
            {
                continue;
            }

            if (query.ValueKind == JsonValueKind.String)
            {
                yield return query.GetString()!;
            }
            else if (query.ValueKind == JsonValueKind.Object &&
                     query.TryGetProperty("query", out var text) &&
                     text.ValueKind == JsonValueKind.String)
            {
                yield return text.GetString()!;
            }
        }
    }

    /// <summary>
    /// Extracts the metric names from one PromQL expression by removing everything that is not
    /// one: quoted label values, label matcher blocks, range selectors, and the parenthesised
    /// label lists of <c>by</c>/<c>on</c>/<c>ignoring</c>. What remains is a token stream in which
    /// the only identifiers that are not keywords are metric names.
    /// </summary>
    private static IEnumerable<string> MetricNamesIn(string expression)
    {
        var text = Regex.Replace(expression, "\"[^\"]*\"", " ");
        text = Regex.Replace(text, @"\{[^{}]*\}", " ");
        text = Regex.Replace(text, @"\[[^\]]*\]", " ");
        text = Regex.Replace(text, @"\blabel_values\s*\([^)]*\)", " ");
        text = Regex.Replace(text, @"\b(?:by|without|on|ignoring|group_left|group_right)\s*\([^)]*\)", " ");

        return Regex.Matches(text, @"(?<![\w.])([a-zA-Z_][a-zA-Z0-9_]*)(?![\w])")
            .Select(match => match.Groups[1].Value)
            .Where(token => !PromQlWords.Contains(token));
    }

    /// <summary>
    /// <c>true</c>/<c>false</c> when the element declares a datasource, <c>null</c> when it
    /// inherits one from its parent.
    /// </summary>
    private static bool? PrometheusDatasource(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("datasource", out var datasource) ||
            datasource.ValueKind != JsonValueKind.Object ||
            !datasource.TryGetProperty("type", out var type))
        {
            return null;
        }

        return type.ValueKind == JsonValueKind.String && type.GetString() == "prometheus";
    }

    /// <summary>Panels of a dashboard, including panels nested inside a collapsed row.</summary>
    private static IEnumerable<JsonElement> Panels(JsonElement container)
    {
        foreach (var panel in Children(container, "panels"))
        {
            yield return panel;

            foreach (var nested in Panels(panel))
            {
                yield return nested;
            }
        }
    }

    private static IEnumerable<JsonElement> Children(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var child in value.EnumerateArray())
        {
            yield return child;
        }
    }

    [Theory]
    // The three names the business alert rules depend on. Pinned explicitly so that renaming an
    // instrument or changing its unit fails here with an obvious message.
    [InlineData("fuelflow_jobs_failed_jobs_total")]
    [InlineData("fuelflow_fulfillment_failed_orders_total")]
    [InlineData("fuelflow_vouchers_pool_available_vouchers")]
    [InlineData("fuelflow_auth_refresh_events_total")]
    [InlineData("fuelflow_payments_stuck_pending_payment")]
    [InlineData("fuelflow_fuel_prices_last_sync_age_seconds")]
    public void AlertTargetedMetrics_KeepTheirExportedNames(string expectedName)
    {
        GetExportedMetricNames().Should().Contain(expectedName);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        // Stop at the repo root, marked by a ".git" entry. In a normal clone that entry is a
        // directory; in a git *worktree* it is a FILE (a gitdir pointer), so accept either —
        // otherwise the walk runs off the top of the drive and the assertion below fails whenever
        // the tests are run from a worktree checkout.
        while (dir is not null &&
               !Directory.Exists(Path.Combine(dir.FullName, ".git")) &&
               !File.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the tests must run from inside the repository");
        return dir!.FullName;
    }
}

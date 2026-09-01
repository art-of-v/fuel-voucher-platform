using FuelFlow.SharedKernel.Options;
using Serilog;
using Serilog.Configuration;
using Serilog.Sinks.Grafana.Loki;

namespace FuelFlow.SharedKernel.Observability;

/// <summary>
/// Shared Serilog wiring for the API and the JobsWorker so both emit identically shaped logs.
/// </summary>
public static class LoggingSetup
{
    /// <summary>
    /// Applies the common enrichers and sinks.
    /// <para>
    /// Labels are deliberately limited to service/environment/level. Loki indexes by label
    /// combination, so adding anything unbounded (user id, order id, route) would create a new
    /// stream per value and degrade the whole instance. High-cardinality values belong in the log
    /// body, which stays queryable without being indexed.
    /// </para>
    /// </summary>
    public static LoggerConfiguration ConfigureFuelFlowLogging(
        this LoggerConfiguration configuration,
        ObservabilityOptions observability)
    {
        configuration
            .Enrich.FromLogContext()
            .Enrich.WithProperty("service", observability.ServiceName)
            .Enrich.WithProperty("environment", observability.Environment)
            .Enrich.WithProperty("host", Environment.MachineName);

        // Compact JSON lets Loki index structured fields instead of regex-parsing text.
        // Kept opt-in so local console output stays readable.
        if (observability.StructuredLogs)
        {
            configuration.WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter());
        }
        else
        {
            configuration.WriteTo.Console();
        }

        if (observability.Loki.Enabled)
        {
            configuration.WriteTo.GrafanaLoki(
                observability.Loki.Url,
                labels:
                [
                    new LokiLabel { Key = "service", Value = observability.ServiceName },
                    new LokiLabel { Key = "environment", Value = observability.Environment }
                ],
                // 'level' is added automatically by handleLogLevelAsLabel. Nothing else is
                // promoted: every other property stays an unindexed structured field.
                // TraceId/SpanId are emitted as structured metadata so a log line can be
                // pivoted to its trace once a trace backend exists, without becoming a label
                // (a per-trace stream would be catastrophic for cardinality).
                traceIdMode: LokiFieldDestination.StructuredMetadata,
                spanIdMode: LokiFieldDestination.StructuredMetadata,
                batchSizeLimit: observability.Loki.BatchPostingLimit,
                queueLimit: observability.Loki.QueueLimit);
        }

        return configuration;
    }
}

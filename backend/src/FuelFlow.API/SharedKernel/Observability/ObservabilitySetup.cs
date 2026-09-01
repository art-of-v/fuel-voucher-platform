using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace FuelFlow.SharedKernel.Observability;

public static class ObservabilitySetup
{
    /// <summary>
    /// Wires OpenTelemetry metrics (and optionally traces) for either host.
    /// <para>
    /// Exporters are opt-in: with the default configuration nothing is exported and
    /// the process has no dependency on a running collector.
    /// </para>
    /// </summary>
    public static IServiceCollection AddFuelFlowObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration
            .GetSection(ObservabilityOptions.SectionName)
            .Get<ObservabilityOptions>() ?? new ObservabilityOptions();

        services.AddSingleton<FuelFlowMetrics>();
        services.AddFuelFlowAlerting(configuration);

        var resource = ResourceBuilder.CreateDefault()
            .AddService(
                serviceName: options.ServiceName,
                serviceVersion: typeof(ObservabilitySetup).Assembly.GetName().Version?.ToString() ?? "unknown",
                serviceInstanceId: Environment.MachineName)
            .AddAttributes(
            [
                // Labels every metric/trace so a single Grafana + Telegram group can
                // serve multiple environments without ambiguity.
                new KeyValuePair<string, object>("deployment.environment", options.Environment),
                new KeyValuePair<string, object>("host.name", Environment.MachineName)
            ]);

        var otel = services.AddOpenTelemetry().ConfigureResource(_ => { });

        otel.WithMetrics(metrics =>
        {
            metrics
                .SetResourceBuilder(resource)
                .AddMeter(ObservabilityConstants.MeterName)
                // Npgsql publishes connection-pool and command metrics through its own
                // meter; Npgsql.OpenTelemetry only supplies tracing instrumentation.
                .AddMeter("Npgsql")
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                // Supplies process.cpu.time / process.memory.usage so Grafana can alert
                // on host-level resource pressure without a node exporter.
                .AddProcessInstrumentation();

            if (options.Prometheus.Enabled)
            {
                metrics.AddPrometheusExporter();
            }

            if (options.Otlp.Enabled)
            {
                metrics.AddOtlpExporter((exporter, _) => ConfigureOtlp(exporter, options.Otlp));
            }
        });

        // The ActivitySource is always registered so TraceId/SpanId flow into logs.
        // Only the exporter is gated, keeping the door open for Tempo later.
        otel.WithTracing(tracing =>
        {
            tracing
                .SetResourceBuilder(resource)
                .AddSource(ObservabilityConstants.ActivitySourceName)
                .AddAspNetCoreInstrumentation(o =>
                {
                    o.RecordException = true;
                    // Scrape and probe traffic would otherwise dominate the trace volume.
                    o.Filter = ctx =>
                        !ctx.Request.Path.StartsWithSegments("/metrics")
                        && !ctx.Request.Path.StartsWithSegments("/health");
                })
                .AddHttpClientInstrumentation(o => o.RecordException = true)
                .AddNpgsql();

            if (options.Otlp is { Enabled: true, EnableTraces: true })
            {
                tracing.SetSampler(new ParentBasedSampler(
                    new TraceIdRatioBasedSampler(options.Otlp.TraceSampleRatio)));

                OpenTelemetry.Trace.OtlpTraceExporterHelperExtensions.AddOtlpExporter(
                    tracing,
                    exporter => ConfigureOtlp(exporter, options.Otlp));
            }
        });

        return services;
    }

    /// <summary>
    /// Registers the alert notifier. Falls back to a no-op implementation unless
    /// Telegram is both enabled and fully configured, so call sites can always
    /// resolve <see cref="IAlertNotifier"/>.
    /// </summary>
    public static IServiceCollection AddFuelFlowAlerting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var telegram = configuration
            .GetSection(TelegramOptions.SectionName)
            .Get<TelegramOptions>() ?? new TelegramOptions();

        if (!telegram.IsConfigured)
        {
            services.AddSingleton<IAlertNotifier, NullAlertNotifier>();
            services.AddTransient<NotificationDispatcher>();
            return services;
        }

        services.AddHttpClient<IAlertNotifier, TelegramAlertNotifier>(client =>
        {
            client.BaseAddress = new Uri(telegram.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(telegram.TimeoutSeconds);
        });

        services.AddTransient<NotificationDispatcher>();

        return services;
    }

    private static void ConfigureOtlp(
        OtlpExporterOptions exporter,
        ObservabilityOptions.OtlpOptions otlp)
    {
        exporter.Endpoint = new Uri(otlp.Endpoint);
        exporter.Protocol = otlp.Protocol.Equals("httpprotobuf", StringComparison.OrdinalIgnoreCase)
            ? OtlpExportProtocol.HttpProtobuf
            : OtlpExportProtocol.Grpc;
    }
}

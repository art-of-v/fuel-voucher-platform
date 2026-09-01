namespace FuelFlow.SharedKernel.Options;

public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    /// <summary>
    /// Logical service name reported to metrics/logs/traces (e.g. "fuelflow-api").
    /// </summary>
    public string ServiceName { get; set; } = "fuelflow";

    /// <summary>
    /// Deployment environment label attached to every metric, log and alert
    /// (e.g. "Development", "Staging", "Production"). Alert messages are prefixed
    /// with this so a single Telegram group can serve multiple environments.
    /// </summary>
    public string Environment { get; set; } = "Development";

    /// <summary>
    /// Emit logs as compact JSON instead of the human-readable console template.
    /// Enable in containers so Loki can parse structured fields; leave off locally.
    /// </summary>
    public bool StructuredLogs { get; set; } = false;

    public OtlpOptions Otlp { get; set; } = new();
    public PrometheusOptions Prometheus { get; set; } = new();
    public LokiOptions Loki { get; set; } = new();

    public sealed class LokiOptions
    {
        /// <summary>
        /// Pushes logs to Loki directly from the app.
        /// <para>
        /// Needed because Alloy collects logs by scraping the Docker socket, which only sees
        /// containers. When the API/worker run on the host (Visual Studio, dotnet run) their
        /// logs never reach Loki at all. This sink makes local and containerised runs behave
        /// identically instead of silently losing host logs.
        /// </para>
        /// </summary>
        public bool Enabled { get; set; } = false;

        public string Url { get; set; } = "http://localhost:3100";

        /// <summary>
        /// Sink failures must never take down the app, so delivery is best-effort: the sink
        /// buffers in memory and drops on sustained backpressure rather than blocking.
        /// </summary>
        public int BatchPostingLimit { get; set; } = 200;

        public int QueueLimit { get; set; } = 10000;
    }

    public sealed class OtlpOptions
    {
        /// <summary>
        /// When false (default) no OTLP exporter is registered, so the app has no
        /// dependency on a running collector.
        /// </summary>
        public bool Enabled { get; set; } = false;

        public string Endpoint { get; set; } = "http://localhost:4317";

        /// <summary>"grpc" (default, port 4317) or "httpprotobuf" (port 4318).</summary>
        public string Protocol { get; set; } = "grpc";

        /// <summary>Export traces in addition to metrics. Requires a trace backend (Tempo).</summary>
        public bool EnableTraces { get; set; } = false;

        /// <summary>
        /// Fraction of traces sampled (0.0-1.0). Errors are always recorded regardless.
        /// </summary>
        public double TraceSampleRatio { get; set; } = 0.1;
    }

    public sealed class PrometheusOptions
    {
        /// <summary>Exposes a /metrics scrape endpoint.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Port for the JobsWorker's standalone metrics listener. Ignored by the API,
        /// which serves /metrics on its normal port.
        /// </summary>
        public int WorkerPort { get; set; } = 9091;
    }
}

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace FuelFlow.SharedKernel.Observability;

/// <summary>
/// Names shared by the metric/trace instrumentation. Kept in one place so the
/// Grafana dashboards and alert rules can be kept in sync with the code.
/// </summary>
public static class ObservabilityConstants
{
    public const string MeterName = "FuelFlow";
    public const string ActivitySourceName = "FuelFlow";
}

/// <summary>
/// Business metrics for FuelFlow. Registered as a singleton; inject where the
/// corresponding domain event happens.
/// <para>
/// Infrastructure metrics (request rate, GC, EF query duration) come free from the
/// OpenTelemetry instrumentation packages and are deliberately not duplicated here.
/// </para>
/// </summary>
public sealed class FuelFlowMetrics : IDisposable
{
    private readonly Meter _meter;

    // Vouchers
    private readonly Counter<long> _vouchersAssigned;
    private readonly Counter<long> _vouchersImported;
    private readonly Counter<long> _vouchersGifted;
    private readonly Counter<long> _vouchersRecalled;
    private readonly Counter<long> _vouchersBlocked;

    // Fulfillment
    private readonly Counter<long> _fulfillmentSucceeded;
    private readonly Counter<long> _fulfillmentFailed;
    private readonly Histogram<double> _fulfillmentDuration;

    // Orders
    private readonly Counter<long> _ordersCreated;
    private readonly Histogram<double> _orderPendingPaymentAge;

    // Monobank
    private readonly Counter<long> _monobankInvoiceCreated;
    private readonly Counter<long> _monobankInvoiceFailed;
    private readonly Histogram<double> _monobankInvoiceDuration;
    private readonly Counter<long> _monobankWebhookReceived;
    private readonly Counter<long> _monobankWebhookFailed;
    private readonly Histogram<double> _monobankWebhookLag;

    // Hangfire
    private readonly Counter<long> _jobSucceeded;
    private readonly Counter<long> _jobFailed;
    private readonly Histogram<double> _jobDuration;

    public FuelFlowMetrics()
    {
        _meter = new Meter(ObservabilityConstants.MeterName);

        _vouchersAssigned = _meter.CreateCounter<long>(
            "fuelflow.vouchers.assigned", "vouchers",
            "Vouchers assigned to a user through order fulfillment.");

        _vouchersImported = _meter.CreateCounter<long>(
            "fuelflow.vouchers.imported", "vouchers",
            "Vouchers added to the pool via admin import.");

        _vouchersGifted = _meter.CreateCounter<long>(
            "fuelflow.vouchers.gifted", "vouchers",
            "Company vouchers gifted to a worker.");

        _vouchersRecalled = _meter.CreateCounter<long>(
            "fuelflow.vouchers.recalled", "vouchers",
            "Gifted vouchers recalled back into the company pool.");

        _vouchersBlocked = _meter.CreateCounter<long>(
            "fuelflow.vouchers.blocked", "vouchers",
            "Vouchers blocked as a result of firing a worker.");

        _fulfillmentSucceeded = _meter.CreateCounter<long>(
            "fuelflow.fulfillment.succeeded", "orders",
            "Orders fulfilled successfully.");

        _fulfillmentFailed = _meter.CreateCounter<long>(
            "fuelflow.fulfillment.failed", "orders",
            "Orders that failed fulfillment. Alerting target.");

        _fulfillmentDuration = _meter.CreateHistogram<double>(
            "fuelflow.fulfillment.duration", "ms",
            "Time taken to fulfill a single order.");

        _ordersCreated = _meter.CreateCounter<long>(
            "fuelflow.orders.created", "orders",
            "Orders created at checkout.");

        _orderPendingPaymentAge = _meter.CreateHistogram<double>(
            "fuelflow.orders.pending_payment_age", "s",
            "Age of orders still awaiting payment. Sustained high values indicate a stuck payment flow.");

        _monobankInvoiceCreated = _meter.CreateCounter<long>(
            "fuelflow.monobank.invoice.created", "invoices",
            "Monobank invoices created successfully.");

        _monobankInvoiceFailed = _meter.CreateCounter<long>(
            "fuelflow.monobank.invoice.failed", "invoices",
            "Monobank invoice creation failures. Alerting target.");

        _monobankInvoiceDuration = _meter.CreateHistogram<double>(
            "fuelflow.monobank.invoice.duration", "ms",
            "Latency of the Monobank invoice creation call.");

        _monobankWebhookReceived = _meter.CreateCounter<long>(
            "fuelflow.monobank.webhook.received", "webhooks",
            "Monobank webhooks received.");

        _monobankWebhookFailed = _meter.CreateCounter<long>(
            "fuelflow.monobank.webhook.failed", "webhooks",
            "Monobank webhooks that failed validation or processing.");

        _monobankWebhookLag = _meter.CreateHistogram<double>(
            "fuelflow.monobank.webhook.lag", "s",
            "Delay between invoice creation and webhook receipt.");

        _jobSucceeded = _meter.CreateCounter<long>(
            "fuelflow.jobs.succeeded", "jobs",
            "Hangfire jobs that completed successfully.");

        _jobFailed = _meter.CreateCounter<long>(
            "fuelflow.jobs.failed", "jobs",
            "Hangfire jobs that threw. Alerting target.");

        _jobDuration = _meter.CreateHistogram<double>(
            "fuelflow.jobs.duration", "ms",
            "Hangfire job execution time.");
    }

    /// <summary>
    /// Registers an observable gauge reporting remaining pool vouchers per
    /// station/fuel type. The callback is polled by the metrics exporter, so it must
    /// be cheap and must not throw.
    /// </summary>
    public void RegisterVoucherPoolGauge(Func<IEnumerable<Measurement<int>>> observe)
    {
        _meter.CreateObservableGauge(
            "fuelflow.vouchers.pool_available",
            observe,
            "vouchers",
            "Unassigned vouchers remaining per station and fuel type. Primary business alert.");
    }

    /// <summary>
    /// Registers an observable gauge reporting order counts per status.
    /// </summary>
    public void RegisterOrderStatusGauge(Func<IEnumerable<Measurement<int>>> observe)
    {
        // No unit: the exporter appends it to the name, and "orders" here would yield the
        // stuttering fuelflow_orders_by_status_orders. The name already says what is counted.
        _meter.CreateObservableGauge(
            "fuelflow.orders.by_status",
            observe,
            description: "Current order count grouped by status.");
    }

    public void VoucherAssigned(string provider, string fuelTypeId, bool isCompany) =>
        _vouchersAssigned.Add(1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("fuel_type", fuelTypeId),
            new KeyValuePair<string, object?>("ownership", isCompany ? "company" : "personal"));

    public void VouchersImported(string provider, int count) =>
        _vouchersImported.Add(count, new KeyValuePair<string, object?>("provider", provider));

    public void VouchersGifted(int count) => _vouchersGifted.Add(count);

    public void VoucherRecalled() => _vouchersRecalled.Add(1);

    public void VouchersBlocked(int count) => _vouchersBlocked.Add(count);

    public void FulfillmentSucceeded(double elapsedMs)
    {
        _fulfillmentSucceeded.Add(1);
        _fulfillmentDuration.Record(elapsedMs);
    }

    public void FulfillmentFailed(string reason) =>
        _fulfillmentFailed.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public void OrderCreated(bool isCompany) =>
        _ordersCreated.Add(1,
            new KeyValuePair<string, object?>("ownership", isCompany ? "company" : "personal"));

    public void RecordPendingPaymentAge(double ageSeconds) =>
        _orderPendingPaymentAge.Record(ageSeconds);

    public void MonobankInvoiceCreated(double elapsedMs)
    {
        _monobankInvoiceCreated.Add(1);
        _monobankInvoiceDuration.Record(elapsedMs);
    }

    public void MonobankInvoiceFailed(string reason) =>
        _monobankInvoiceFailed.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public void MonobankWebhookReceived(string status) =>
        _monobankWebhookReceived.Add(1, new KeyValuePair<string, object?>("status", status));

    public void MonobankWebhookFailed(string reason) =>
        _monobankWebhookFailed.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public void RecordWebhookLag(double lagSeconds) => _monobankWebhookLag.Record(lagSeconds);

    public void JobSucceeded(string jobName, double elapsedMs)
    {
        // Tagged job_name, not job: Prometheus reserves "job" for the scrape target and
        // silently renames any colliding metric label to "exported_job", which makes
        // dashboard and alert queries written against "job" match nothing.
        _jobSucceeded.Add(1, new KeyValuePair<string, object?>("job_name", jobName));
        _jobDuration.Record(elapsedMs, new KeyValuePair<string, object?>("job_name", jobName));
    }

    public void JobFailed(string jobName, string exceptionType) =>
        _jobFailed.Add(1,
            new KeyValuePair<string, object?>("job_name", jobName),
            new KeyValuePair<string, object?>("exception", exceptionType));

    public void Dispose() => _meter.Dispose();
}

/// <summary>
/// Shared ActivitySource. Registered even when trace export is disabled so that
/// TraceId/SpanId exist on logs from day one - enabling Tempo later then makes
/// historical logs already correlatable.
/// </summary>
public static class FuelFlowActivitySource
{
    public static readonly ActivitySource Instance = new(ObservabilityConstants.ActivitySourceName);
}

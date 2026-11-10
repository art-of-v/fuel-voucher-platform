using System.Diagnostics.Metrics;
using FluentAssertions;
using FuelFlow.Features.Feedback;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Options;
using FuelFlow.UnitTests.Observability;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FuelFlow.UnitTests.Feedback;

/// <summary>
/// What the periodic webhook check publishes: the one number the FeedbackWebhookMissing alert
/// thresholds.
/// </summary>
/// <remarks>
/// Measured on 2026-11-10: rotating the feedback bot's token at BotFather left the bot with no
/// webhook at all, and every message a customer sent it expired in Telegram's queue while every
/// endpoint of ours answered correctly. These pin the mapping from Telegram's answer to the gauge,
/// and the two states that must not depend on Telegram at all: the feature being off, and the
/// feature being switched on without a token to ask with.
/// </remarks>
[Collection(BusinessGaugeCollection.Name)]
public sealed class FeedbackWebhookHealthMonitorTests : IDisposable
{
    private readonly FuelFlowMetrics _metrics = new();

    public void Dispose() => _metrics.Dispose();

    [Fact]
    public async Task A_registered_webhook_reports_zero()
    {
        var bot = new StubFeedbackBot(new TelegramWebhookStatus(
            "https://api.palne.shop/api/telegram/feedback/webhook", false, 0, null, null));

        await Monitor(bot, enabled: true).CheckWebhookAsync();

        Samples().Should().ContainSingle().Which.Should().Be(0);
    }

    [Fact]
    public async Task An_empty_url_reports_one()
    {
        // The production shape on 2026-11-10: getWebhookInfo answers ok:true with `"url": ""`, and
        // two updates are already waiting for a delivery that will never happen.
        var bot = new StubFeedbackBot(new TelegramWebhookStatus(string.Empty, false, 2, null, null));

        await Monitor(bot, enabled: true).CheckWebhookAsync();

        Samples().Should().ContainSingle().Which.Should().Be(1);
    }

    [Fact]
    public async Task A_disabled_feature_is_not_missing_and_does_not_ask_Telegram()
    {
        var bot = new StubFeedbackBot(new TelegramWebhookStatus(string.Empty, false, 0, null, null));

        await Monitor(bot, enabled: false).CheckWebhookAsync();

        Samples().Should().ContainSingle().Which.Should().Be(0,
            "the channel is off on purpose; a webhook that is also absent is not an incident");
        bot.Calls.Should().Be(0);
    }

    [Fact]
    public async Task An_enabled_but_unprovisioned_bot_is_missing_without_a_Telegram_call()
    {
        var bot = new StubFeedbackBot(new TelegramWebhookStatus("https://example.invalid", false, 0, null, null));

        await Monitor(bot, enabled: true, configured: false).CheckWebhookAsync();

        Samples().Should().ContainSingle().Which.Should().Be(1,
            "with no secret the endpoint rejects every delivery and with no token nothing can be sent back");
        bot.Calls.Should().Be(0);
    }

    [Fact]
    public async Task A_failure_to_ask_Telegram_publishes_nothing_and_propagates()
    {
        var bot = new StubFeedbackBot(exception: new HttpRequestException("telegram is down"));

        var act = () => Monitor(bot, enabled: true).CheckWebhookAsync();

        await act.Should().ThrowAsync<HttpRequestException>();
        Samples().Should().BeEmpty(
            "'could not ask' is not 'missing', and it is not healthy either - the job fails and JobFailed reports it");
    }

    private FeedbackWebhookHealthMonitor Monitor(StubFeedbackBot bot, bool enabled, bool configured = true) =>
        new(
            bot,
            _metrics,
            Options.Create(new TelegramFeedbackOptions
            {
                Enabled = enabled,
                BotToken = configured ? "123456:TESTTOKEN" : string.Empty,
                WebhookSecret = configured ? "unit-test-secret" : string.Empty,
                SigningKey = configured ? "unit-test-signing-key-not-a-secret" : string.Empty,
                StaffChatId = configured ? -1001234567890 : 0
            }),
            NullLogger<FeedbackWebhookHealthMonitor>.Instance);

    /// <summary>
    /// The values the exporter would collect for this gauge, read through a listener so the test
    /// sees exactly what a scrape would.
    /// </summary>
    private List<int> Samples()
    {
        var values = new List<int>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == ObservabilityConstants.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<int>((instrument, value, _, _) =>
        {
            if (instrument.Name == "fuelflow.telegram.feedback_webhook_missing")
            {
                values.Add(value);
            }
        });

        listener.Start();
        listener.RecordObservableInstruments();

        return values;
    }

    private sealed class StubFeedbackBot : ITelegramFeedbackClient
    {
        private readonly TelegramWebhookStatus? _status;
        private readonly Exception? _exception;

        public StubFeedbackBot(TelegramWebhookStatus? status = null, Exception? exception = null)
        {
            _status = status;
            _exception = exception;
        }

        public int Calls { get; private set; }

        public Task<long?> SendMessageAsync(
            long chatId,
            string text,
            string? parseMode = null,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task SetWebhookAsync(string url, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<TelegramWebhookStatus> GetWebhookStatusAsync(CancellationToken ct = default)
        {
            Calls++;

            return _exception is not null
                ? Task.FromException<TelegramWebhookStatus>(_exception)
                : Task.FromResult(_status!);
        }
    }
}

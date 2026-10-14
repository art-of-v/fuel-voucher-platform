using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.Features.Feedback;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FuelFlow.UnitTests.Feedback;

/// <summary>
/// What the client actually puts on the wire for <c>sendMessage</c>.
/// <para>
/// These tests exist because of a production outage that no mock could have caught. The
/// <c>parse_mode</c> parameter was added as a plain property on an anonymous object, so a call that
/// wanted *no* formatting still serialised <c>"parse_mode": null</c> — and Telegram answers that with
/// <c>400 Bad Request: unsupported parse_mode</c>. Every message that is not the staff post died: the
/// welcome, the acknowledgement, a staff reply relayed to a customer. The staff post kept working,
/// because its value ("HTML") is valid, which made it read as a Telegram-side problem.
/// </para>
/// A recording <see cref="HttpMessageHandler"/> is used rather than a mocked interface precisely
/// because the defect lived in the JSON shape, between the client and the wire.
/// </summary>
public sealed class TelegramFeedbackClientSendPayloadTests
{
    private const string Token = "123456:TESTTOKEN";

    /// <summary>
    /// Long enough to satisfy IsConfigured's minimum, and worded so it cannot be mistaken for a real
    /// key - gitleaks flags any high-entropy `SigningKey = "..."` literal, which is the correct
    /// instinct even for a fixture.
    /// </summary>
    private const string SigningKey = "unit-test-signing-key-not-a-secret";

    /// <summary>Captures the request body the client produced, and answers with a Telegram-shaped ok.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        public int StatusCode { get; set; } = (int)HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Body = request.Content!.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();

            var json = StatusCode == (int)HttpStatusCode.OK
                ? """{"ok":true,"result":{"message_id":42}}"""
                : """{"ok":false,"error_code":400,"description":"Bad Request: unsupported parse_mode"}""";

            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)StatusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private static (TelegramFeedbackClient Client, CapturingHandler Handler) Build()
    {
        var handler = new CapturingHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.telegram.org") };

        var options = Options.Create(new TelegramFeedbackOptions
        {
            Enabled = true,
            BotToken = Token,
            BotUsername = "palne_feedback_bot",
            WebhookSecret = "secret",
            StaffChatId = -1001234567890,
            SigningKey = SigningKey
        });

        return (new TelegramFeedbackClient(http, options, NullLogger<TelegramFeedbackClient>.Instance), handler);
    }

    private static JsonElement SendPayload(string? body) => JsonDocument.Parse(body!).RootElement;

    [Fact]
    public async Task PlainTextSendOmitsParseModeEntirely()
    {
        // The regression: Telegram rejects a present-but-null parse_mode, so the property must not
        // appear in the JSON at all for the welcome, the acknowledgement and a relayed staff reply.
        var (client, handler) = Build();

        await client.SendMessageAsync(42, "Дякуємо, повідомлення отримано.");

        var payload = SendPayload(handler.Body);
        payload.TryGetProperty("parse_mode", out _).Should().BeFalse(
            "\"parse_mode\": null is not 'no parse mode' to Telegram - it is a 400");
        payload.GetProperty("text").GetString().Should().Be("Дякуємо, повідомлення отримано.");
    }

    [Fact]
    public async Task StaffPostSendIncludesHtmlParseMode()
    {
        var (client, handler) = Build();

        await client.SendMessageAsync(-1001234567890, "<b>Idea</b>", "HTML");

        var payload = SendPayload(handler.Body);
        payload.GetProperty("parse_mode").GetString().Should().Be("HTML");
    }

    [Fact]
    public async Task AnEmptyParseModeIsTreatedAsAbsentRatherThanSent()
    {
        // A blank string is not a mode Telegram knows either, and it is just as fatal as null.
        var (client, handler) = Build();

        await client.SendMessageAsync(42, "Привіт", "   ");

        SendPayload(handler.Body).TryGetProperty("parse_mode", out _).Should().BeFalse();
    }

    [Fact]
    public async Task EverySendCarriesTheChatIdAndThePreviewGuard()
    {
        // The two properties that must survive the move to a dictionary alongside parse_mode.
        var (client, handler) = Build();

        await client.SendMessageAsync(42, "тест");

        var payload = SendPayload(handler.Body);
        payload.GetProperty("chat_id").GetInt64().Should().Be(42);
        payload.GetProperty("disable_web_page_preview").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ARejectedSendIsReportedAsNullRatherThanThrowing()
    {
        // The contract the webhook depends on: it has already persisted the row, and an exception
        // here would make Telegram redeliver an update that was in fact received.
        var (client, handler) = Build();
        handler.StatusCode = (int)HttpStatusCode.BadRequest;

        var delivered = await client.SendMessageAsync(42, "Дякуємо, повідомлення отримано.");

        delivered.Should().BeNull();
    }
}
using System.Net;
using System.Threading.Channels;
using FluentAssertions;
using FuelFlow.Features.ErrorLogs.Logging;
using FuelFlow.SharedKernel.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Xunit;

namespace FuelFlow.UnitTests.Observability;

/// <summary>
/// Planning #793: the Bot API takes its credential in the request path, and the framework's
/// <c>System.Net.Http.HttpClient</c> logging writes that path at Information level. Before the
/// redaction the token reached the console and Loki on every alert - seven days of retention, and
/// anyone who could read the box could post to the alerting group with it.
/// </summary>
public sealed class TelegramTokenRedactionTests
{
    private const string BotId = "7788991234";
    private const string Secret = "AAHfake-token_value-0123456789abcdef";
    private const string Token = BotId + ":" + Secret;
    private const string MaskedToken = BotId + ":<redacted>";

    [Theory]
    [InlineData(
        "https://api.telegram.org/bot7788991234:AAHfake-token_value-0123456789abcdef/sendMessage",
        "https://api.telegram.org/bot7788991234:<redacted>/sendMessage")]
    [InlineData(
        "/bot7788991234:AAHfake-token_value-0123456789abcdef/setWebhook",
        "/bot7788991234:<redacted>/setWebhook")]
    public void RedactTelegramBotToken_MasksTheSecretAndKeepsTheBotId(string input, string expected)
    {
        SensitiveDataRedactor.RedactTelegramBotToken(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("https://api.telegram.org/")]
    [InlineData("/bot7788991234/sendMessage")]
    [InlineData("order 1234: paid")]
    [InlineData("")]
    public void RedactTelegramBotToken_LeavesTextWithoutATokenAlone(string input)
    {
        SensitiveDataRedactor.RedactTelegramBotToken(input).Should().Be(input);
    }

    [Fact]
    public async Task HttpClientLogging_WhenTheUriCarriesTheToken_RendersItMasked()
    {
        // The leak is the framework's own line, not ours, so the test drives a real
        // IHttpClientFactory: that is what installs LoggingHttpMessageHandler and produces
        // "System.Net.Http.HttpClient.IAlertNotifier.LogicalHandler" with the full URI.
        var events = new List<LogEvent>();
        using var serilog = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.With(new TelegramTokenRedactingEnricher())
            .WriteTo.Sink(new CollectingSink(events))
            .CreateLogger();

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(serilog, dispose: false);
        });
        services
            .AddHttpClient("IAlertNotifier", client => client.BaseAddress = new Uri("https://api.telegram.org"))
            .ConfigurePrimaryHttpMessageHandler(() => new StubTelegramHandler());

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("IAlertNotifier");

        using var response = await client.PostAsync($"/bot{Token}/sendMessage", new StringContent("{}"));

        response.IsSuccessStatusCode.Should().BeTrue();
        events.Should().Contain(
            e => Render(e).Contains("HttpClient.IAlertNotifier", StringComparison.Ordinal),
            "the framework's request logging is the line that leaked");
        events.Select(Render).Should().NotContain(
            line => line.Contains(Secret, StringComparison.Ordinal),
            "the credential must not survive any rendered line");
        events.Select(Render).Should().Contain(
            line => line.Contains($"bot{MaskedToken}", StringComparison.Ordinal),
            "the URI stays readable, minus the token");
    }

    [Fact]
    public void ErrorLog_WhenTheFailureTextCarriesTheToken_StoresItMasked()
    {
        // The error_logs table is fed by a separate provider that never sees the Serilog event,
        // so it has to redact on its own - the message cell, the exception message and the stack.
        var channel = Channel.CreateUnbounded<ErrorLogEntry>();
        var logger = new DatabaseLogger(
            "System.Net.Http.HttpClient.IAlertNotifier.LogicalHandler",
            channel.Writer,
            new ServiceCollection().BuildServiceProvider());

        logger.LogError(
            new HttpRequestException(
                $"Request to https://api.telegram.org/bot{Token}/sendMessage failed"),
            "Telegram alert failed for {Uri}",
            $"/bot{Token}/sendMessage");

        channel.Reader.TryRead(out var entry).Should().BeTrue("the journal row should have been queued");

        entry!.Message.Should().Contain($"bot{MaskedToken}").And.NotContain(Secret);
        entry.ExceptionMessage.Should().Contain($"bot{MaskedToken}").And.NotContain(Secret);
        entry.StackTrace.Should().NotContain(Secret);
    }

    private static string Render(LogEvent logEvent)
    {
        using var writer = new StringWriter();
        new CompactJsonFormatter().Format(logEvent, writer);
        return writer.ToString();
    }

    private sealed class CollectingSink(List<LogEvent> events) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => events.Add(logEvent);
    }

    private sealed class StubTelegramHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}")
            });
    }
}

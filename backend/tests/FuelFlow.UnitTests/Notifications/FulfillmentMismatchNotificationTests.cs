using FluentAssertions;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace FuelFlow.UnitTests.Notifications;

/// <summary>
/// #217 step 3: releasing a fulfillment loses nothing - the voucher goes back to stock and the
/// delivery record is kept - but it is still the job rewriting the operator's own orders, so it has
/// to say so rather than doing it silently. These cover the flag contract and the payload; the
/// Telegram delivery path itself is already covered elsewhere.
/// </summary>
public sealed class FulfillmentMismatchNotificationTests
{
    private static NotificationDispatcher Create(
        bool channelEnabled,
        bool flag,
        Mock<IAlertNotifier> notifier)
        => new(
            notifier.Object,
            Options.Create(new TelegramOptions
            {
                Enabled = channelEnabled,
                BotToken = "token",
                ChatIds = ["chat"],
                Notifications = new TelegramOptions.NotificationOptions
                {
                    Vouchers = new TelegramOptions.VoucherNotificationOptions { NotifyOnFulfillmentRelease = flag },
                },
            }),
            Mock.Of<ILogger<NotificationDispatcher>>());

    private sealed class Sent
    {
        public string? Title;
        public string? Message;
        public IReadOnlyDictionary<string, string>? Context;
    }

    private static (Mock<IAlertNotifier> Notifier, Sent Sent) Capturing()
    {
        var sent = new Sent();

        var notifier = new Mock<IAlertNotifier>();
        notifier
            .Setup(n => n.SendAsync(
                It.IsAny<AlertSeverity>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Callback<AlertSeverity, string, string, IReadOnlyDictionary<string, string>?, bool, CancellationToken>(
                (_, t, m, c, _, _) =>
                {
                    sent.Title = t;
                    sent.Message = m;
                    sent.Context = c;
                })
            .Returns(Task.CompletedTask);

        return (notifier, sent);
    }

    [Fact]
    public async Task The_Alert_Names_The_Order_And_Both_Counts()
    {
        var (notifier, sent) = Capturing();

        await Create(channelEnabled: true, flag: true, notifier)
            .FulfillmentMismatchAsync(
                "11111111-2222-3333-4444-555555555555", "11111111", "okko", 2, 3, 5, corrected: true, default);

        sent.Title.Should().NotBeNullOrWhiteSpace();
        sent.Message.Should().NotBeNullOrWhiteSpace();

        // The operator's first move is to open that order - if the id is missing, the alert is noise.
        sent.Message.Should().Contain("11111111");
        sent.Message.Should().Contain("okko");

        sent.Context.Should().NotBeNull();
        sent.Context!["Мережа"].Should().Be("okko");
        sent.Context["Повернуто"].Should().Be("2");
        sent.Context["Очікувалося"].Should().Be("3");
        sent.Context["Знайдено"].Should().Be("5");
        sent.Context["Дія"].Should().Be("виправлено автоматично");
    }

    [Fact]
    public async Task An_Uncorrected_Mismatch_Says_So_Rather_Than_Claiming_A_Return()
    {
        var (notifier, sent) = Capturing();

        await Create(channelEnabled: true, flag: true, notifier)
            .FulfillmentMismatchAsync(
                "66666666-0000-0000-0000-000000000000", "66666666", "okko", 0, 3, 5,
                corrected: false, default);

        sent.Context!["Дія"].Should().Be("залишено людині");
        sent.Context["Повернуто"].Should().Be("0");

        // The message must not say vouchers went back to stock - nothing did, and an operator who
        // believed it would look for stock that is still gone.
        sent.Message.Should().NotContain("повернуто на склад");
        sent.Message.Should().Contain("вручну");
    }

    [Fact]
    public async Task Nothing_Is_Sent_When_The_Flag_Is_Off()
    {
        var (notifier, _) = Capturing();

        await Create(channelEnabled: true, flag: false, notifier)
            .FulfillmentMismatchAsync("22222222-0000-0000-0000-000000000000", "22222222", "okko", 2, 3, 5, corrected: true, default);

        notifier.Verify(n => n.SendAsync(
            It.IsAny<AlertSeverity>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_Repeated_Release_For_The_Same_Order_Is_Throttled()
    {
        var (notifier, _) = Capturing();
        var dispatcher = Create(channelEnabled: true, flag: true, notifier);

        await dispatcher.FulfillmentMismatchAsync("33333333-0000-0000-0000-000000000000", "33333333", "okko", 2, 3, 5, corrected: true, default);
        await dispatcher.FulfillmentMismatchAsync("33333333-0000-0000-0000-000000000000", "33333333", "okko", 2, 3, 5, corrected: true, default);

        // The throttle is keyed per order, not globally: one bad order must not be able to silence
        // the alert about a different one, while a job that keeps re-running over the same order
        // should not repeat itself every cycle.
        notifier.Verify(n => n.SendAsync(
            It.IsAny<AlertSeverity>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_Different_Order_Is_Not_Silenced_By_Another_One()
    {
        var (notifier, _) = Capturing();
        var dispatcher = Create(channelEnabled: true, flag: true, notifier);

        await dispatcher.FulfillmentMismatchAsync("44444444-0000-0000-0000-000000000000", "44444444", "okko", 2, 3, 5, corrected: true, default);
        await dispatcher.FulfillmentMismatchAsync("55555555-0000-0000-0000-000000000000", "55555555", "okko", 1, 4, 5, corrected: true, default);

        notifier.Verify(n => n.SendAsync(
            It.IsAny<AlertSeverity>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }
}
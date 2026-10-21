using System.Collections.Concurrent;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Time;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FuelFlow.SharedKernel.Observability;

/// <summary>
/// Single place where domain events are turned into alerts. Call sites raise an
/// intent ("a payment failed") and this decides whether it is enabled, at what
/// severity, and how it is worded.
/// <para>
/// Keeping the config checks here rather than at each call site means a new toggle
/// never requires touching a handler, and the toggles cannot drift apart in how they
/// are interpreted. Every method swallows its own errors: alerting must never fail
/// the request or job that triggered it.
/// </para>
/// </summary>
public sealed class NotificationDispatcher
{
    /// <summary>
    /// Last send time per throttle key. Static and process-local: a restart
    /// intentionally re-arms everything, and losing the history is harmless because
    /// the worst case is one extra message.
    /// </summary>
    private static readonly ConcurrentDictionary<string, DateTime> LastSentUtc = new();

    private readonly IAlertNotifier _alerts;
    private readonly TelegramOptions.NotificationOptions _config;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(
        IAlertNotifier alerts,
        IOptions<TelegramOptions> telegram,
        ILogger<NotificationDispatcher> logger)
    {
        _alerts = alerts;
        _config = telegram.Value.Notifications;
        _logger = logger;
    }

    /// <summary>
    /// A dispatcher with every notification switched off. For tests and any other
    /// caller that constructs a service directly rather than through DI, so they do
    /// not have to build a notifier and options just to be ignored.
    /// </summary>
    public static NotificationDispatcher Disabled { get; } = new(
        new NullAlertNotifier(),
        Microsoft.Extensions.Options.Options.Create(new TelegramOptions()),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<NotificationDispatcher>.Instance);

    public Task NewUserRegisteredAsync(Guid userId, string phoneNumber, CancellationToken ct = default)
        => SendAsync(
            _config.NotifyOnNewUser,
            AlertSeverity.Info,
            "Новий користувач зареєстрований",
            "Номер телефону вперто підтверджено.",
            new Dictionary<string, string>
            {
                ["ID користувача"] = userId.ToString(),
                ["Телефон"] = SensitiveDataRedactor.MaskPhoneNumber(phoneNumber)
            },
            throttleKey: null,
            ct);

    public Task CompanyCreatedAsync(Guid legalEntityId, string name, string edrpou, CancellationToken ct = default)
        => SendAsync(
            _config.NotifyOnCompanyCreated,
            AlertSeverity.Info,
            "Створено профіль компанії",
            $"Для {name} створено профіль юридичної особи.",
            new Dictionary<string, string>
            {
                ["ID юридичної особи"] = legalEntityId.ToString(),
                ["Назва"] = name,
                ["ЄДРОПУ"] = edrpou
            },
            throttleKey: null,
            ct);

    public Task PaymentSucceededAsync(Guid orderId, decimal amount, CancellationToken ct = default)
        => SendAsync(
            _config.NotifyOnPaymentSuccess,
            AlertSeverity.Info,
            "Оплата успішна",
            $"Оплату за замовлення {orderId} підтверджено.",
            new Dictionary<string, string>
            {
                ["ID замовлення"] = orderId.ToString(),
                ["Сума"] = amount.ToString("0.00")
            },
            throttleKey: null,
            ct);

    public Task PaymentFailedAsync(Guid orderId, string status, CancellationToken ct = default)
        => SendAsync(
            _config.NotifyOnPaymentFailure,
            AlertSeverity.Warning,
            "Оплата не пройшла",
            $"Оплату за замовлення {orderId} не завершено.",
            new Dictionary<string, string>
            {
                ["ID замовлення"] = orderId.ToString(),
                ["Статус"] = status
            },
            throttleKey: null,
            ct);

    /// <summary>
    /// One summary per import rather than one message per bad row: a malformed batch
    /// of several hundred vouchers would otherwise be unusable as a notification.
    /// </summary>
    public Task ImportCompletedWithErrorsAsync(
        int importedCount,
        int errorCount,
        IReadOnlyList<string> sampleErrors,
        CancellationToken ct = default)
    {
        var context = new Dictionary<string, string>
        {
            ["Імпортовано"] = importedCount.ToString(),
            ["Помилок"] = errorCount.ToString()
        };

        for (var i = 0; i < sampleErrors.Count && i < 5; i++)
        {
            context[$"Помилка {i + 1}"] = Truncate(sampleErrors[i], 200);
        }

        if (errorCount > sampleErrors.Count)
        {
            context["Примітка"] = $"Ще {errorCount - sampleErrors.Count} помилок(и) не показано";
        }

        return SendAsync(
            _config.NotifyOnImportErrors && errorCount > 0,
            AlertSeverity.Warning,
            "Імпорт талонів завершено з помилками",
            $"{errorCount} талон(ів) відхилено під час імпорту.",
            context,
            throttleKey: null,
            ct);
    }

    public Task VoucherStockLowAsync(
        string brand,
        string fuelType,
        int available,
        int threshold,
        CancellationToken ct = default)
    {
        var vouchers = _config.Vouchers;
        var isZero = available == 0;
        var enabled = isZero ? vouchers.NotifyOnZeroStock : vouchers.NotifyOnLowStock;

        return SendAsync(
            enabled,
            isZero ? AlertSeverity.Critical : AlertSeverity.Warning,
            isZero ? "Талонів не залишилось" : "Малий залишок талонів",
            isZero
                ? $"Для {brand} талонів більше немає."
                : $"Для {brand} залишилось лише {available} талон(ів).",
            new Dictionary<string, string>
            {
                ["Мережа"] = brand,
                ["Пальне"] = fuelType,
                ["Доступно"] = available.ToString(),
                ["Порог"] = threshold.ToString()
            },
            throttleKey: $"stock|{brand}|{fuelType}|{isZero}",
            ct,
            throttleMinutes: vouchers.ReminderIntervalMinutes);
    }

    /// <summary>
    /// Raised when sellable stock is about to stop being sellable. Warning rather than Critical: a
    /// paying customer is not waiting yet, but the fuel is walking towards a written-off value and
    /// the remedy - pricing it for a paid renewal - only works while it is still alive.
    /// </summary>
    public Task VoucherStockExpiringAsync(
        string brand,
        string fuelType,
        int expiring,
        int available,
        int withinDays,
        CancellationToken ct = default)
    {
        var vouchers = _config.Vouchers;

        return SendAsync(
            vouchers.NotifyOnExpiringStock,
            AlertSeverity.Warning,
            "Талони добігають терміну",
            $"Для {brand} {expiring} талон(ів) із {available} доступних втрачають силу менш ніж за {withinDays} дн. "
            + "Їх треба або продати, або залишити на платне продовження.",
            new Dictionary<string, string>
            {
                ["Мережа"] = brand,
                ["Пальне"] = fuelType,
                ["Добігають терміну"] = expiring.ToString(),
                ["Доступно"] = available.ToString(),
                ["Горизонт, дн"] = withinDays.ToString()
            },
            // Distinct throttle key from the low-count alert: the same combination can be both
            // comfortably stocked and quietly running out of life, and the two deserve a message
            // each rather than one swallowing the other.
            throttleKey: $"expiring|{brand}|{fuelType}",
            ct,
            throttleMinutes: vouchers.ReminderIntervalMinutes);
    }

    /// <summary>
    /// Raised when a paid order cannot be filled because no matching voucher exists.
    /// Critical rather than Warning: a customer has already been charged and is
    /// waiting, so this needs someone to act rather than just be recorded.
    /// </summary>
    public Task OrderUnfulfillableAsync(
        Guid orderId,
        string fuelType,
        int assigned,
        int needed,
        CancellationToken ct = default)
        => SendAsync(
            _config.Vouchers.NotifyOnOrderUnfulfillable,
            AlertSeverity.Critical,
            "Замовлення неможливо виконати — бракує талонів",
            $"Замовлення {orderId} не забезпечене талонами і не може бути виконане.",
            new Dictionary<string, string>
            {
                ["ID замовлення"] = orderId.ToString(),
                ["Пальне"] = fuelType,
                ["Присвоєно"] = assigned.ToString(),
                ["Потрібно"] = needed.ToString()
            },
            throttleKey: $"unfulfillable|{orderId}|{fuelType}",
            ct,
            throttleMinutes: 60);

    /// <summary>
    /// Throttled per exception type + endpoint, because a single broken endpoint under
    /// a client retry loop would otherwise flood the chat and hide everything else.
    /// </summary>
    /// <summary>
    /// Raised when Monobank's cancel list holds a refund confirmation that matches no refund we
    /// sent, and there was more than one thing it could have been. #261: the old code took the
    /// newest entry regardless, so a confirmation belonging to a different refund could complete
    /// the wrong one and move the order to PartiallyRefunded.
    /// <para>
    /// Warning, not Critical: the refund simply stays Processing and retries, and no money has
    /// moved. It is worth a human because the retry will not fix it — only knowing which refund the
    /// bank meant will.
    /// </para>
    /// </summary>
    public Task RefundSyncUnmatchedAsync(
        Guid refundId,
        Guid orderId,
        string invoiceId,
        int entryCount,
        int siblingRefunds,
        CancellationToken ct = default)
        => SendAsync(
            _config.NotifyOnUnmatchedRefundSync,
            AlertSeverity.Warning,
            "Підтвердження повернення не зіставилося",
            $"Monobank повернув підтвердження для рахунку {invoiceId}, але жоден із наших refund не має такого extRef. "
            + $"Записів у cancel list: {entryCount}, інших refund на цьому рахунку, що очікують: {siblingRefunds}. "
            + "Повернення лишається Processing і повторить спробу — потрібно звірити, який саме refund мав на увазі банк.",
            new Dictionary<string, string>
            {
                ["Refund"] = refundId.ToString(),
                ["Замовлення"] = orderId.ToString(),
                ["Рахунок"] = invoiceId
            },
            // Per invoice: one alert explains the whole list, and repeated syncs of the same stuck
            // refund must not send a message each time. Hourly, like the unfulfillable alert - a
            // stuck refund is a thing to look at today, not something to page about every cycle.
            throttleKey: $"refundsync|{invoiceId}",
            ct,
            throttleMinutes: 60);

    public Task UnhandledExceptionAsync(Exception exception, string endpoint, CancellationToken ct = default)
        => SendAsync(
            _config.NotifyOnUnhandledException,
            AlertSeverity.Critical,
            "Необроблена помилка",
            Truncate(exception.Message, 500),
            new Dictionary<string, string>
            {
                ["Тип"] = exception.GetType().Name,
                ["Ендпоінт"] = endpoint
            },
            throttleKey: $"ex|{exception.GetType().FullName}|{endpoint}",
            ct,
            throttleMinutes: _config.ExceptionThrottleMinutes);

    /// <summary>
    /// Raised when a supplier+fuel's customer price/л has been forced BELOW its blended supplier
    /// cost/л by the discount the customer is granted (a term, pricing epic slice 3). Two shapes:
    /// <list type="bullet">
    /// <item><b>deliberate</b> — a manager has opted this supplier+fuel in (loss-leader); the sale
    /// is allowed but the standing decision is worth recording (Warning).</item>
    /// <item><b>emergent</b> — NOT opted in, so the hard block now refuses every sale/activation
    /// until pricing is fixed; a paying funnel is stalled, so this is Critical.</item>
    /// </list>
    /// Throttled per fuel + shape so a burst of cost entries or blocked checkouts sends one message.
    /// </summary>
    /// <param name="costPerLiter">
    /// What we pay per litre, or NULL when nobody has stated it. A null is a real state - the catalogue
    /// import creates fuels with no cost on purpose, because it knows the column price and nothing about
    /// what we pay - so the message says so rather than printing a zero that reads like a number.
    /// </param>
    public Task BelowCostAsync(
        string brand,
        string fuelType,
        decimal? costPerLiter,
        decimal finalPerLiter,
        bool deliberate,
        CancellationToken ct = default)
        => SendAsync(
            _config.NotifyOnBelowCost,
            deliberate ? AlertSeverity.Warning : AlertSeverity.Critical,
            deliberate ? "Увімкнено продаж нижче собівартості" : "Ціна нижче собівартості — продаж заблоковано",
            deliberate
                ? $"{brand} / {fuelType} продається нижче собівартості за рішенням менеджера; кожен літр — свідома втрата."
                : $"{brand} / {fuelType} продається нижче собівартості, тому продаж і видача заблоковані, доки ціну не виправлять або менеджер не ввімкне дозвіл.",
            new Dictionary<string, string>
            {
                ["Мережа"] = brand,
                ["Пальне"] = fuelType,
                ["Собівартість, грн/л"] = costPerLiter?.ToString("0.00") ?? "?",
                ["Ціна, грн/л"] = finalPerLiter.ToString("0.00"),
                ["Режим"] = deliberate ? "свідомий продаж дешевше" : "заблоковано"
            },
            throttleKey: $"belowcost|{fuelType}|{deliberate}",
            ct,
            throttleMinutes: 60);

    /// <summary>
    /// Raised when the fulfillment reconciliation found an order whose fulfillments disagree with
    /// its line items. Warning, not Critical: no fuel is lost either way - the vouchers go back to
    /// the warehouse and the delivery record is kept - but this is the job overwriting the
    /// operator's own orders, and it used to do so silently (#217).
    /// <para>
    /// <paramref name="releasedVouchers"/> is zero for an order that was left alone, which is the
    /// case that most needs a human: nothing was corrected automatically, so the mismatch stays put
    /// until someone opens the order. Repeats for one order mean the reconciliation is probably
    /// reading a wrong quantity rather than finding a real mismatch.
    /// </para>
    /// </summary>
    public Task FulfillmentMismatchAsync(
        string orderId,
        string orderNumber,
        string brand,
        int releasedVouchers,
        int wantedTotal,
        int foundTotal,
        bool corrected,
        CancellationToken ct = default)
    {
        var vouchers = _config.Vouchers;

        return SendAsync(
            vouchers.NotifyOnFulfillmentRelease,
            AlertSeverity.Warning,
            corrected ? "Продовження видачі повернуло талони на склад" : "Розходження у видачі замовлення",
            corrected
                ? $"Замовлення {orderNumber} ({brand}) мало {wantedTotal} талон(ів), але в базі було {foundTotal}. "
                    + $"{releasedVouchers} талон(ів) повернуто на склад, запис про видачу збережено як звільнений."
                : $"Замовлення {orderNumber} ({brand}) мало {wantedTotal} талон(ів), але в базі було {foundTotal}. "
                    + "Замовлення старше за вікно звірки, тому нічого не змінено - розходження треба перевірити вручну.",
            new Dictionary<string, string>
            {
                ["Замовлення"] = orderNumber,
                ["ID"] = orderId,
                ["Мережа"] = brand,
                ["Очікувалося"] = wantedTotal.ToString(),
                ["Знайдено"] = foundTotal.ToString(),
                ["Повернуто"] = releasedVouchers.ToString(),
                ["Дія"] = corrected ? "виправлено автоматично" : "залишено людині"
            },
            // Per-order key, so one bad order cannot swallow the alert about a different one - and
            // a job that keeps re-running over the same order is throttled rather than repeated
            // every cycle.
            throttleKey: $"mismatch|{orderId}",
            ct,
            throttleMinutes: vouchers.ReminderIntervalMinutes);
    }

    private async Task SendAsync(
        bool enabled,
        AlertSeverity severity,
        string title,
        string message,
        IReadOnlyDictionary<string, string> context,
        string? throttleKey,
        CancellationToken ct,
        int throttleMinutes = 0)
    {
        if (!enabled)
        {
            return;
        }

        if (throttleKey is not null && throttleMinutes > 0 && !TryClaimSlot(throttleKey, throttleMinutes))
        {
            return;
        }

        try
        {
            // respectMinimumSeverity: false - the per-event toggle above is the gate.
            // Otherwise an Info-level event such as "new user" would be silently
            // dropped by the default Warning floor despite being explicitly enabled.
            await _alerts.SendAsync(severity, title, message, context, respectMinimumSeverity: false, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispatch notification {Title}", title);
        }
    }

    private static bool TryClaimSlot(string key, int throttleMinutes)
    {
        var now = SystemClock.UtcNow;
        var interval = TimeSpan.FromMinutes(throttleMinutes);
        var claimed = false;

        // Whether the slot was claimed is decided by the branch that writes, NOT by comparing the
        // stored timestamp with "now": on a clock that does not move between two calls (the test
        // suite's fixed instant, or two calls inside one clock tick) the stored value and "now"
        // are the same value, and a comparison would report a slot that was never re-claimed as
        // claimed - which is how a throttled alert repeated itself.
        LastSentUtc.AddOrUpdate(
            key,
            _ =>
            {
                claimed = true;
                return now;
            },
            (_, previous) =>
            {
                if (now - previous >= interval)
                {
                    claimed = true;
                    return now;
                }

                return previous;
            });

        return claimed;
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "...";
}

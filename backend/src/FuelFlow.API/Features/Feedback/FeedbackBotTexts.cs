namespace FuelFlow.Features.Feedback;

/// <summary>
/// The fixed Ukrainian copy the bot sends on its own. Kept in one place because every string is
/// user-visible product text: it has to read as us, and it has to stay consistent between the
/// greeting a bound user sees and the one an unbound one sees.
/// <para>
/// The bot also speaks Russian, because that is what customers of a Ukrainian fuel app actually
/// write in. Language follows the customer, not the locale.
/// </para>
/// </summary>
internal static class FeedbackBotTexts
{
    /// <summary>Sent on a valid signed /start: the customer came from the app, so we know who they are.</summary>
    public const string WelcomeBound =
        "Вітаємо! 👋\n\n" +
        "Напишіть, будь ласка, що саме не працює або чого бракує в застосунку — подробиці, " +
        "скриншот текстом, номер екрана. Ваше повідомлення прочитає команда розробки.\n\n" +
        "Це приватний чат: інші користувачі його не бачать.";

    /// <summary>Sent on /start with a missing, forged or expired payload - somebody found the bot by name.</summary>
    public const string WelcomeUnbound =
        "Вітаємо! 👋\n\n" +
        "Напишіть, будь ласка, що саме не працює або чого бракує в застосунку. Ваше повідомлення " +
        "прочитає команда розробки.\n\n" +
        "Щоб ми бачили ваш акаунт, відкрийте «Зворотний зв'язок» у застосунку — тоді чат " +
        "прив'яжеться до нього автоматично.";

    public const string Help =
        "Як я працюю:\n\n" +
        "• Напишіть, що не працює, чого бракує або що незручно — чим конкретніше, тим краще.\n" +
        "• Привітання, жарти й тести я ігнорую: у команду йдуть лише повідомлення по суті.\n" +
        "• Це приватний чат — інші користувачі його не бачать.\n" +
        "• Відповідь людини прийде сюди ж, у цей чат.";

    /// <summary>Acknowledges every message, including junk. Silence after someone typed reads as a
    /// dropped message, and the one thing that would make a customer write again is believing we
    /// did not hear them.</summary>
    public const string Acknowledge = "Дякуємо, повідомлення отримано.";

    public const string Unavailable =
        "Бот тимчасово недоступний. Напишіть, будь ласка, на support@palne.shop — ми прочитаємо.";

    /// <summary>Shown when a chat exceeds the hourly ceiling. Says what to do, so a legitimate
    /// customer reporting a burst of problems is not simply silenced.</summary>
    public const string RateLimited =
        "Дякуємо! Зараз ви написали дуже багато повідомлень. Зачекайте трохи — або напишіть " +
        "усе одним повідомленням, щоб ми прочитали одразу.";

    public const string TooLong =
        "Повідомлення задовге. Напишіть, будь ласка, головне — ми прочитаємо.";

    /// <summary>Header the staff group sees above each triage-passing message. Sent with
    /// parse_mode HTML, so every interpolated value is escaped - see <see cref="Escape"/>.</summary>
    /// <param name="categoryLabel">Null while the classifier is disabled, in which case the
    /// bracket block is dropped rather than filled with a placeholder: a staff member reading
    /// "[без категорії]" learns nothing, whereas a bare "…0203" reads as "no triage yet" and is
    /// exactly what the post genuinely knows.</param>
    public static string StaffPost(
        string? categoryLabel,
        string? severityLabel,
        string author,
        string summary,
        string originalText,
        string? appVersion,
        string? platform)
    {
        var head = categoryLabel is null || severityLabel is null
            ? $"🟡 {Escape(author)}"
            : $"🟡 [<b>{Escape(categoryLabel)}</b> · <b>{Escape(severityLabel)}</b>] {Escape(author)}";

        var lines = new List<string> { head, string.Empty, $"<i>{Escape(summary)}</i>" };

        if (appVersion is not null || platform is not null)
        {
            // An em dash for the unknown half, not "?": a question mark reads as a value Telegram
            // could not render, an em dash reads as "we did not collect it".
            lines.Add(string.Empty);
            lines.Add($"Застосунок: {Escape(platform ?? "—")} {Escape(appVersion ?? "—")}");
        }

        lines.Add(string.Empty);
        lines.Add($"<blockquote>{Escape(TrimForPost(originalText))}</blockquote>");

        return string.Join('\n', lines);
    }

    private static string TrimForPost(string text) =>
        text.Length <= 900 ? text : string.Concat(text.AsSpan(0, 900), "…");

    /// <summary>
    /// HTML escaping for the staff post. The customer's own text goes into an HTML-formatted
    /// message, so an unescaped "&lt;" would make Telegram reject the whole post with an opaque
    /// 400 - dropping exactly the message that happened to contain angle brackets.
    /// </summary>
    private static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FuelFlow.UnitTests.Architecture;

/// <summary>
/// Architecture guard for the production exception alert.
/// </summary>
/// <remarks>
/// The review of the live production database (2026-10-20) found that an unhandled exception in
/// production was <b>invisible</b>: <c>GlobalExceptionHandler</c> has always called
/// <c>UnhandledExceptionAsync</c>, and the dispatcher has always had the method, and the
/// <c>NotifyOnUnhandledException</c> flag has always existed - it was simply set to
/// <c>false</c> in <c>appsettings.Production.json</c>. An exception in production was a log line
/// on the box, and nobody reads the box's logs at three in the morning.
///
/// The wiring is not the thing this guard checks. It checks the three things that have to be true
/// TOGETHER, because each of them alone is a configuration that looks fine:
///
/// <list type="number">
/// <item>the flag is <b>on</b> in production - otherwise the alert never fires;</item>
/// <item>the throttle is <b>positive</b> - an exception in a loop would otherwise send one
/// message per occurrence, and the chat becomes useless within a minute;</item>
/// <item>the dispatcher actually <b>uses</b> that throttle - the config value is only a number
/// until something passes it to the sender, and a throttle nobody applies is a flood.</item>
/// </list>
///
/// A guard that reads a value out of a JSON file is unusual, and it is here because the failure it
/// prevents is silent: flipping the flag off does not break a build, does not fail a test, and
/// does not change a single log line. It just quietly stops the alerting, and the first person
/// to notice is the one whose exception nobody was told about.
/// </remarks>
public sealed class UnhandledExceptionAlertGuardTests
{
    [Fact]
    public void Production_Should_Alert_On_An_Unhandled_Exception()
    {
        var notifications = ReadProductionNotifications();

        notifications.TryGetProperty("NotifyOnUnhandledException", out var flag).Should().BeTrue(
            "the production configuration is where the flag lives, and it has to be there for the "
            + "alert to fire at all");
        flag.GetBoolean().Should().BeTrue(
            "an unhandled exception in production is a log line nobody reads - the alert is the "
            + "only thing that tells anybody, and it is off by default in the options class, so "
            + "production has to say so out loud");
    }

    [Fact]
    public void The_Exception_Alert_Should_Be_Throttled()
    {
        var notifications = ReadProductionNotifications();

        notifications.TryGetProperty("ExceptionThrottleMinutes", out var minutes).Should().BeTrue(
            "the throttle is configured next to the flag it belongs to");
        minutes.GetInt32().Should().BeGreaterThan(0,
            "an exception in a loop sends one message per occurrence, and an unthrottled alert "
            + "drowns the chat exactly when it matters most");

        // The config value is only a number until the dispatcher passes it to the sender.
        var dispatcher = WithoutComments(ReadDispatcher());
        dispatcher.Should().MatchRegex(
            @"throttleKey:\s*\$""ex\|",
            "the alert must be keyed by the exception type and the endpoint, or one noisy endpoint "
            + "silences every other one");
        dispatcher.Should().Contain("throttleMinutes: _config.ExceptionThrottleMinutes",
            "the configured throttle has to reach the sender, or the configuration is decoration");
    }

    /// <summary>
    /// The comments are stripped first: the dispatcher's own comment names the throttle it
    /// applies, and a guard that read the comment would pass on a dispatcher that stopped
    /// applying it.
    /// </summary>
    private static string WithoutComments(string source)
    {
        var withoutBlockComments = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        return Regex.Replace(withoutBlockComments, @"//[^\n]*", " ");
    }

    private static JsonElement ReadProductionNotifications()
    {
        var text = ReadRepositoryFile(Path.Combine("src", "FuelFlow.API", "appsettings.Production.json"));

        // The file carries comments, which JSON does not allow and System.Text.Json does not
        // read, so they are stripped before parsing. A comment is a line whose first non-space
        // character is a slash.
        var withoutComments = string.Join(
            "\n",
            text.Split('\n')
                .Select(line => line.TrimStart().StartsWith("//") ? string.Empty : line));

        var root = JsonDocument.Parse(withoutComments).RootElement;

        // Telegram -> Notifications: the flags are the per-event switches of the Telegram
        // channel, and the channel is the thing that carries them.
        root.TryGetProperty("Telegram", out var telegram).Should().BeTrue(
            "the notification flags live under Telegram in the production configuration");
        telegram.TryGetProperty("Notifications", out var notifications).Should().BeTrue(
            "the per-event switches live under Notifications in the Telegram section");
        return notifications.Clone();
    }

    private static string ReadDispatcher() =>
        ReadRepositoryFile(Path.Combine("src", "FuelFlow.API", "SharedKernel", "Observability", "NotificationDispatcher.cs"));

    private static string ReadRepositoryFile(string relativePath, [CallerFilePath] string thisFile = "")
    {
        // thisFile = .../backend/tests/FuelFlow.UnitTests/Architecture/<this>.cs
        var backend = Directory.GetParent(thisFile)!.Parent!.Parent!.Parent!.FullName;
        var path = Path.Combine(backend, relativePath);
        if (File.Exists(path)) return File.ReadAllText(path);

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"{relativePath} was not found from the test assembly.");
    }
}

using System.Diagnostics;
using System.Threading.Channels;
using FluentAssertions;
using FuelFlow.Features.ErrorLogs.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FuelFlow.UnitTests.Observability;

/// <summary>
/// The admin error log is a table, so the reason a request faulted has to be readable in the
/// message cell - before anyone expands a row - and every record emitted while serving that one
/// request has to carry the same trace id so the rows can be told apart as a single incident.
/// </summary>
public sealed class DatabaseLoggerTests
{
    [Fact]
    public void Log_WhenTheFormatterDropsTheException_AppendsTheCauseToTheMessage()
    {
        // Regression for the GET /api/company/invitations 500 that sat in the journal as
        // "Unhandled exception for GET /api/company/invitations" with no hint of what failed.
        // Every ILogger pipeline hands over a formatter that renders the template and ignores
        // its Exception argument, so the message cell is all the journal shows at a glance.
        var entry = Log(logger => logger.LogError(
            new InvalidOperationException("The LINQ expression could not be translated"),
            "Unhandled exception for {Method} {Path}", "GET", "/api/company/invitations"));

        entry.Message.Should().Be(
            "Unhandled exception for GET /api/company/invitations — "
            + "System.InvalidOperationException: The LINQ expression could not be translated");
    }

    [Fact]
    public void Log_WhenTheTemplateAlreadyCarriesTheException_AppendsNothing()
    {
        // EF Core 10 puts the exception into its own template through an {error} hole, so Serilog
        // renders the whole thing - type, message and stack - into the message itself. Appending
        // would print the same text twice and push the useful part out of the 4000-char budget.
        var error = new DbUpdateException("An error occurred while saving the entity changes.");
        var rendered = "An exception occurred in the database while saving changes for context type "
            + "'FuelFlow.Persistence.ApplicationDbContext'.\n"
            + error + "\n   at Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync()";

        var entry = LogPreRendered(rendered, error);

        entry.Message.Should().Be(rendered);
    }

    [Fact]
    public void Log_WhenTheExceptionMessageSpansLines_FlattensItOntoOneLine()
    {
        var entry = Log(logger => logger.LogError(
            new InvalidOperationException("first line\n  second line\r\n\tthird line"),
            "Unhandled exception"));

        entry.Message.Should().Be(
            "Unhandled exception — System.InvalidOperationException: first line second line third line");
    }

    [Fact]
    public void Log_WhenTheCauseIsHuge_KeepsTheMessageWithinItsBudget()
    {
        var entry = Log(logger => logger.LogError(
            new InvalidOperationException(new string('x', 50_000)), "Unhandled exception"));

        entry.Message.Length.Should().BeLessThanOrEqualTo(4000);
        entry.ExceptionMessage.Should().HaveLength(2000, "the full text stays in its own column");
    }

    [Fact]
    public void Log_WithoutAnException_LeavesTheMessageUntouched()
    {
        var entry = Log(logger => logger.LogError("Voucher import failed for file {File}", "one-time.pdf"));

        entry.Message.Should().Be("Voucher import failed for file one-time.pdf");
        entry.ExceptionType.Should().BeNull();
    }

    [Fact]
    public void Log_InsideAnActivity_StampsTheActivityTraceId()
    {
        using var activity = new Activity("test").Start();

        var first = Log(logger => logger.LogError(new InvalidOperationException("boom"), "first"));
        var second = Log(logger => logger.LogError(new InvalidOperationException("boom"), "second"));

        first.TraceId.Should().Be(activity.TraceId.ToString());
        second.TraceId.Should().Be(first.TraceId, "rows of one request must group together");
    }

    [Fact]
    public void Log_WithNoActivity_StampsTheHttpTraceIdentifier()
    {
        // Hosting can serve a request without starting an activity; the connection-based
        // identifier is then the only thing that ties that request's rows together.
        var entry = Log(
            logger => logger.LogError(new InvalidOperationException("boom"), "first"),
            http: new DefaultHttpContext { TraceIdentifier = "0HN7A1B2C3D4E:00000003" });

        entry.TraceId.Should().Be("0HN7A1B2C3D4E:00000003");
    }

    [Fact]
    public void Log_OutsideAnyRequest_LeavesTheTraceIdNull()
    {
        // Startup, shutdown and background jobs have no activity to correlate against; a null
        // keeps them out of an incident filter instead of lumping them under a shared id.
        var entry = Log(logger => logger.LogError(new InvalidOperationException("boom"), "first"));

        entry.TraceId.Should().BeNull();
    }

    [Fact]
    public void Log_ForANonErrorLevel_WritesNothing()
    {
        TryLog(logger => logger.LogInformation("noise")).Should().BeNull();
        TryLog(logger => logger.LogWarning("noise")).Should().BeNull();
    }

    [Fact]
    public void Log_ForAnAbortedRequest_IsDroppedAsNoise()
    {
        TryLog(logger => logger.LogError(
            new OperationCanceledException("The client closed the connection."), "aborted"))
            .Should().BeNull();
    }

    /// <summary>
    /// Runs one log call and returns the single record it produced, so each test asserts on the
    /// exact row the admin journal would receive.
    /// </summary>
    private static ErrorLogEntry Log(Action<DatabaseLogger> act, HttpContext? http = null)
        => TryLog(act, http) ?? throw new InvalidOperationException("No error-log record was written.");

    private static ErrorLogEntry? TryLog(Action<DatabaseLogger> act, HttpContext? http = null)
    {
        var channel = Channel.CreateUnbounded<ErrorLogEntry>();

        var services = new ServiceCollection();
        if (http is not null)
            services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = http });

        act(new DatabaseLogger("Test.Source", channel.Writer, services.BuildServiceProvider()));

        return channel.Reader.TryRead(out var entry) ? entry : null;
    }

    /// <summary>
    /// Feeds the sink a message the formatter has already rendered - the shape Serilog hands
    /// over when a source puts the exception into its own template, such as EF Core's {error}.
    /// </summary>
    private static ErrorLogEntry LogPreRendered(string rendered, Exception? exception)
    {
        var channel = Channel.CreateUnbounded<ErrorLogEntry>();
        var services = new ServiceCollection().BuildServiceProvider();
        var logger = new DatabaseLogger("Microsoft.EntityFrameworkCore.Update", channel.Writer, services);

        logger.Log(
            LogLevel.Error,
            new EventId(1),
            rendered,
            exception,
            (state, _) => state!.ToString());

        channel.Reader.TryRead(out var entry).Should().BeTrue("the record should have been queued");
        return entry!;
    }
}
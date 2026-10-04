using System.Diagnostics;
using System.Security.Claims;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FuelFlow.Features.ErrorLogs.Logging;

internal sealed record ErrorLogEntry(
    Guid Id,
    DateTime LoggedAtUtc,
    string Level,
    string Message,
    string? ExceptionType,
    string? ExceptionMessage,
    string? StackTrace,
    string? Source,
    string? RequestPath,
    string? RequestMethod,
    string? UserName,
    string? TraceId);

/// <summary>
/// Sinks Error/Critical log records into the <c>error_logs</c> table asynchronously.
/// A bounded channel keeps request threads fast while a single background writer
/// persists records; write failures are swallowed so logging never recurses or crashes.
/// </summary>
public sealed class DatabaseLoggerProvider : ILoggerProvider
{
    private readonly IServiceProvider _services;
    private readonly Channel<ErrorLogEntry> _queue;
    private readonly CancellationTokenSource _cts;
    private readonly Task _writerTask;
    private int _disposed;

    public DatabaseLoggerProvider(IServiceProvider services)
    {
        _services = services;
        _queue = Channel.CreateBounded<ErrorLogEntry>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
        _cts = new CancellationTokenSource();
        _writerTask = Task.Run(() => WriteLoopAsync(_cts.Token));
    }

    public ILogger CreateLogger(string categoryName) => new DatabaseLogger(categoryName, _queue.Writer, _services);

    // Dispose must be idempotent. The host tears down logger providers registered in DI
    // more than once (via the LoggerFactory and via the service-provider scope), so this
    // runs twice. The old body called _cts.Cancel() unconditionally; the second pass hit
    // an already-disposed CancellationTokenSource and threw ObjectDisposedException, which
    // xUnit surfaced as a test-class cleanup failure once writeToProviders:true made the
    // provider actually get instantiated. Guard so the teardown runs exactly once.
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _cts.Cancel();
        try
        {
            _writerTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }
        _cts.Dispose();
    }

    private async Task WriteLoopAsync(CancellationToken ct)
    {
        NpgsqlDataSource? dataSource = null;
        try
        {
            dataSource = _services.GetService<NpgsqlDataSource>();
        }
        catch
        {
        }

        try
        {
            await foreach (var entry in _queue.Reader.ReadAllAsync(ct))
            {
                if (dataSource is null)
                    continue;

                try
                {
                    await using var conn = await dataSource.OpenConnectionAsync(ct);
                    await using var cmd = new NpgsqlCommand(
                        """
                        INSERT INTO error_logs
                            (id, logged_at_utc, level, message, exception_type, exception_message, stack_trace, source, request_path, request_method, user_name, trace_id)
                        VALUES
                            (@id, @ts, @level, @message, @exceptionType, @exceptionMessage, @stack, @source, @path, @method, @user, @traceId)
                        """, conn);

                    cmd.Parameters.AddWithValue("id", entry.Id);
                    cmd.Parameters.AddWithValue("ts", entry.LoggedAtUtc);
                    cmd.Parameters.AddWithValue("level", entry.Level);
                    cmd.Parameters.AddWithValue("message", entry.Message);
                    cmd.Parameters.AddWithValue("exceptionType", (object?)entry.ExceptionType ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("exceptionMessage", (object?)entry.ExceptionMessage ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("stack", (object?)entry.StackTrace ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("source", (object?)entry.Source ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("path", (object?)entry.RequestPath ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("method", (object?)entry.RequestMethod ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("user", (object?)entry.UserName ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("traceId", (object?)entry.TraceId ?? DBNull.Value);

                    await cmd.ExecuteNonQueryAsync(ct);
                }
                catch
                {
                    // Never log from the writer: would recurse back into this provider.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
        catch
        {
        }
    }
}

internal sealed class DatabaseLogger : ILogger, ISupportExternalScope
{
    private const int MaxMessageLength = 4000;
    private const int MaxExceptionMessageLength = 2000;
    private const int MaxStackTraceLength = 12000;
    private const int MaxAppendedExceptionLength = 1000;

    private readonly string _categoryName;
    private readonly ChannelWriter<ErrorLogEntry> _writer;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private IExternalScopeProvider? _scopeProvider;

    public DatabaseLogger(string categoryName, ChannelWriter<ErrorLogEntry> writer, IServiceProvider services)
    {
        _categoryName = categoryName;
        _writer = writer;
        _httpContextAccessor = services.GetService<IHttpContextAccessor>();
    }

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopeProvider = scopeProvider;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _scopeProvider?.Push(state);

    public bool IsEnabled(LogLevel logLevel) => logLevel is LogLevel.Error or LogLevel.Critical;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        if (ErrorLogNoiseFilter.IsNoise(logLevel, exception))
            return;

        string message = ComposeMessage(formatter(state, exception), exception);

        string? requestPath = null;
        string? requestMethod = null;
        string? userName = null;
        var http = _httpContextAccessor?.HttpContext;
        if (http is not null)
        {
            requestPath = http.Request.Path.ToString();
            requestMethod = http.Request.Method;
            if (http.User.Identity?.IsAuthenticated == true)
            {
                userName = http.User.FindFirst("first_name")?.Value
                           ?? http.User.FindFirst(ClaimTypes.Name)?.Value
                           ?? http.User.Identity.Name;
            }
        }

        _writer.TryWrite(new ErrorLogEntry(
            Guid.NewGuid(),
            DateTime.UtcNow,
            logLevel.ToString(),
            Truncate(message, MaxMessageLength),
            exception?.GetType().FullName,
            Truncate(exception?.Message, MaxExceptionMessageLength),
            Truncate(exception?.ToString(), MaxStackTraceLength),
            _categoryName,
            requestPath,
            requestMethod,
            userName,
            CurrentTraceId(http)));
    }

    /// <summary>
    /// W3C trace id, so every record emitted while serving one request lands on the same value
    /// and can be grouped back into a single incident. Falls back to the connection-based
    /// <see cref="HttpContext.TraceIdentifier"/> when hosting started no activity for the request,
    /// and to null when there is neither (startup, shutdown, background jobs).
    /// </summary>
    private static string? CurrentTraceId(HttpContext? http)
        => Activity.Current?.TraceId.ToString() ?? http?.TraceIdentifier;

    /// <summary>
    /// The formatter every <see cref="ILogger"/> pipeline hands us renders the message template
    /// and drops its <c>Exception</c> argument - Serilog's provider-collection sink passes
    /// <c>(s, e) =&gt; s.ToString()</c>, and so does Microsoft.Extensions.Logging. So the cause
    /// of a fault reached this log as "Unhandled exception for GET /api/company/invitations" with
    /// no hint of what had actually failed, and the only copy of the reason sat in a column the
    /// admin screen shows only after the row is expanded. Append it to the message itself.
    /// </summary>
    private static string ComposeMessage(string rendered, Exception? exception)
    {
        if (exception is null)
            return rendered;

        var typeName = exception.GetType().FullName ?? exception.GetType().Name;

        // Some sources (EF Core 10) already put the exception into their own template through
        // an {error} hole, so appending would print the same text twice.
        if (rendered.Contains(typeName, StringComparison.Ordinal)
            || (exception.Message.Length > 0 && rendered.Contains(exception.Message, StringComparison.Ordinal)))
        {
            return rendered;
        }

        // Collapsed to one line: a multi-line exception would otherwise shred the table row.
        var detail = OneLine(exception.Message, MaxAppendedExceptionLength);
        var summary = detail.Length == 0 ? typeName : $"{typeName}: {detail}";

        return rendered.Length == 0 ? summary : $"{rendered} — {summary}";
    }

    private static string OneLine(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var collapsed = string.Join(
            ' ',
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return collapsed.Length <= maxLength ? collapsed : collapsed[..maxLength];
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}

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
    string? UserName);

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

    public void Dispose()
    {
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
                            (id, logged_at_utc, level, message, exception_type, exception_message, stack_trace, source, request_path, request_method, user_name)
                        VALUES
                            (@id, @ts, @level, @message, @exceptionType, @exceptionMessage, @stack, @source, @path, @method, @user)
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

        string message = formatter(state, exception);

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
            userName));
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}

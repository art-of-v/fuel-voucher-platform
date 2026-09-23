using System.Security.Claims;
using FluentAssertions;
using FuelFlow.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace FuelFlow.UnitTests.Middleware;

public sealed class RequestLoggingMiddlewareTests
{
    private const string Phone = "+380991234567";
    private const string FirstName = "Ivan";

    [Fact]
    public async Task InvokeAsync_ShouldLogUserId_NotPhoneOrName_WhenAuthenticated()
    {
        var userId = Guid.NewGuid();
        var logger = new CapturingLogger<RequestLoggingMiddleware>();
        var middleware = new RequestLoggingMiddleware(
            ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status200OK;
                return Task.CompletedTask;
            },
            logger);

        var context = CreateHttpContext(AuthenticatedPrincipal(userId));

        await middleware.InvokeAsync(context);

        var responseLog = logger.Entries.Single(e => e.Message.StartsWith("<--"));

        // The user id is the correlatable, non-PII value we want.
        responseLog.GetValue("User").Should().Be(userId.ToString());
        responseLog.Message.Should().Contain(userId.ToString());

        // The raw phone number and first name must never reach the logs.
        responseLog.Message.Should().NotContain(Phone);
        responseLog.Message.Should().NotContain(FirstName);
    }

    [Fact]
    public async Task InvokeAsync_ShouldLogNullUser_WhenUnauthenticated()
    {
        var logger = new CapturingLogger<RequestLoggingMiddleware>();
        var middleware = new RequestLoggingMiddleware(
            ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status200OK;
                return Task.CompletedTask;
            },
            logger);

        var context = CreateHttpContext(new ClaimsPrincipal(new ClaimsIdentity()));

        await middleware.InvokeAsync(context);

        var responseLog = logger.Entries.Single(e => e.Message.StartsWith("<--"));
        responseLog.GetValue("User").Should().BeNull();
    }

    private static DefaultHttpContext CreateHttpContext(ClaimsPrincipal user)
    {
        var context = new DefaultHttpContext { User = user };
        context.Request.Method = HttpMethods.Get;
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost");
        context.Request.Path = "/api/test";
        return context;
    }

    // Mirrors the JWT the app issues: NameIdentifier carries the user id, while a
    // first_name claim and the phone (as the identity Name / ClaimTypes.Name) are
    // the PII values the old logging code would have leaked.
    private static ClaimsPrincipal AuthenticatedPrincipal(Guid userId)
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim("first_name", FirstName),
                new Claim(ClaimTypes.Name, Phone)
            },
            authenticationType: "TestAuth");

        return new ClaimsPrincipal(identity);
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        IReadOnlyList<KeyValuePair<string, object?>> State)
    {
        public object? GetValue(string key)
            => State.FirstOrDefault(kvp => kvp.Key == key).Value;
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var values = state as IReadOnlyList<KeyValuePair<string, object?>>
                         ?? Array.Empty<KeyValuePair<string, object?>>();

            Entries.Add(new LogEntry(logLevel, formatter(state, exception), values));
        }
    }
}

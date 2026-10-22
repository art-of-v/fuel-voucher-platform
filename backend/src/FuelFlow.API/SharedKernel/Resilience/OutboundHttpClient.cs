using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace FuelFlow.SharedKernel.Resilience;

/// <summary>
/// What an outbound client is allowed to do when the provider misbehaves.
/// </summary>
public enum OutboundCallKind
{
    /// <summary>
    /// The client only reads. Every call it makes is safe to repeat, so a transient failure
    /// gets a second attempt.
    /// </summary>
    Read,

    /// <summary>
    /// The client writes. Repeating a call can repeat its effect - a second invoice for one
    /// basket, a second OTP code for one login - so a failure is the caller's to handle and the
    /// pipeline only fails fast when the provider is plainly down.
    /// </summary>
    Write,
}

/// <summary>
/// The one way this application registers an outbound HTTP client.
/// </summary>
/// <remarks>
/// <para>
/// Two things are true of every call this application makes to somebody else's server, and
/// neither of them used to be stated anywhere:
/// </para>
/// <list type="number">
/// <item><description>
/// <b>It has a deadline.</b> The default <see cref="HttpClient.Timeout"/> is 100 seconds, and a
/// provider that never answers holds a request thread for a minute and a half - on the endpoint
/// that creates the invoice. Every client here is registered with an explicit timeout, and the
/// timeout is a parameter of the registration rather than something a caller may forget.
/// </description></item>
/// <item><description>
/// <b>It has a policy.</b> A circuit breaker on every client, so a provider that is down is
/// failed fast instead of being waited on, and a retry with exponential backoff on the clients
/// that only read.
/// </description></item>
/// </list>
/// <para>
/// <b>Why the acquirer is not retried.</b> The retry is a property of the client, and the
/// acquirer's client carries two very different calls: the statement fetch, which is a read, and
/// the invoice creation, which is a POST that charges a customer. A client-level retry cannot tell
/// them apart, and repeating the POST is how one basket becomes two invoices - the exact defect
/// C-1 closed. So the rule is stated per client instead: <see cref="OutboundCallKind.Read"/>
/// clients are retried, <see cref="OutboundCallKind.Write"/> clients are not. The guard test
/// (<c>HttpClientTimeoutGuardTests</c>) checks the declaration against the client's own code, so
/// a client cannot be declared read-only while it posts something.
/// </para>
/// <para>
/// The pipeline itself is Polly's, through <c>Microsoft.Extensions.Http.Resilience</c> - the
/// official resilience stack for <see cref="IHttpClientFactory"/>.
/// </para>
/// </remarks>
public static class OutboundHttpClient
{
    /// <summary>
    /// The timeout a client gets when the caller does not name one. The same shape as the other
    /// providers' defaults in this codebase (30 seconds for the price fetches).
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The deadline for a client whose configured value is missing or not positive.
    /// </summary>
    /// <remarks>
    /// A configured zero used to mean "leave the client's timeout alone", and the client's
    /// default is a hundred seconds - so a zero in the configuration was a way to ask for the
    /// thing this whole change exists to remove. It now means "use the default".
    /// </remarks>
    public static TimeSpan TimeoutOrDefault(int seconds) =>
        seconds > 0 ? TimeSpan.FromSeconds(seconds) : DefaultTimeout;

    /// <summary>
    /// The deadline a client is given. A client without one is the defect this exists to remove,
    /// so the registration refuses it rather than falling back to the hundred seconds.
    /// </summary>
    private static TimeSpan RequireTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout,
                "An outbound client needs a deadline. The HttpClient default is 100 seconds, which "
                + "is how a provider that never answers holds a request thread for a minute and a half.");
        }

        return timeout;
    }

    /// <summary>
    /// How many times a read is repeated after a transient failure. Two retries is three attempts
    /// in total: enough to ride out a provider that blinks, not enough to hold a request open.
    /// </summary>
    private const int MaxRetryAttempts = 2;

    /// <summary>
    /// The first wait between the attempts. The next ones grow from it, exponentially.
    /// </summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// The longest wait between the attempts, whatever the attempt number.
    /// </summary>
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The share of the calls in the window that have to fail before the circuit opens.
    /// </summary>
    private const double CircuitFailureRatio = 0.5;

    /// <summary>
    /// How many calls have to go through the circuit in the window before the share counts. Below
    /// it, two unlucky answers are not a provider that is down.
    /// </summary>
    private const int CircuitMinimumThroughput = 5;

    /// <summary>
    /// The window the failures are counted in.
    /// </summary>
    private static readonly TimeSpan CircuitSamplingDuration = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long the circuit stays open before a single call is let through to ask whether the
    /// provider is back.
    /// </summary>
    private static readonly TimeSpan CircuitBreakDuration = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Registers a named outbound client.
    /// </summary>
    /// <param name="services">The container the client is registered in.</param>
    /// <param name="name">The client's name. One per provider and per merchant.</param>
    /// <param name="timeout">The client's deadline. Required: there is no default here.</param>
    /// <param name="kind">Whether the client only reads, or writes as well.</param>
    /// <param name="configure">Anything else the client needs - a base address, a header.</param>
    public static IHttpClientBuilder AddOutboundHttpClient(
        this IServiceCollection services,
        string name,
        TimeSpan timeout,
        OutboundCallKind kind,
        Action<HttpClient>? configure = null)
    {
        var deadline = RequireTimeout(timeout);

        var builder = services.AddHttpClient(name, client =>
        {
            client.Timeout = deadline;
            configure?.Invoke(client);
        });

        return builder.WithOutboundPolicy(name, kind);
    }

    /// <summary>
    /// Registers a typed outbound client - one type, which is its own interface.
    /// </summary>
    /// <typeparam name="TClient">The client that talks to the provider.</typeparam>
    /// <param name="services">The container the client is registered in.</param>
    /// <param name="timeout">The client's deadline. Required: there is no default here.</param>
    /// <param name="kind">Whether the client only reads, or writes as well.</param>
    /// <param name="configure">Anything else the client needs - a base address, a header.</param>
    public static IHttpClientBuilder AddOutboundHttpClient<TClient>(
        this IServiceCollection services,
        TimeSpan timeout,
        OutboundCallKind kind,
        Action<HttpClient>? configure = null)
        where TClient : class
    {
        var deadline = RequireTimeout(timeout);

        var builder = services.AddHttpClient<TClient>(client =>
        {
            client.Timeout = deadline;
            configure?.Invoke(client);
        });

        return builder.WithOutboundPolicy(typeof(TClient).Name, kind);
    }

    /// <summary>
    /// Registers a typed outbound client.
    /// </summary>
    /// <typeparam name="TClient">The interface the application asks for.</typeparam>
    /// <typeparam name="TImplementation">The client that talks to the provider.</typeparam>
    /// <param name="services">The container the client is registered in.</param>
    /// <param name="timeout">The client's deadline. Required: there is no default here.</param>
    /// <param name="kind">Whether the client only reads, or writes as well.</param>
    /// <param name="configure">Anything else the client needs - a base address, a header.</param>
    public static IHttpClientBuilder AddOutboundHttpClient<TClient, TImplementation>(
        this IServiceCollection services,
        TimeSpan timeout,
        OutboundCallKind kind,
        Action<HttpClient>? configure = null)
        where TClient : class
        where TImplementation : class, TClient
    {
        var deadline = RequireTimeout(timeout);

        var builder = services.AddHttpClient<TClient, TImplementation>(client =>
        {
            client.Timeout = deadline;
            configure?.Invoke(client);
        });

        // The factory names a typed client after its implementation, and the resilience pipeline
        // is keyed by that name - so the two share the circuit breaker of the same provider.
        return builder.WithOutboundPolicy(typeof(TImplementation).Name, kind);
    }

    /// <summary>
    /// Puts the policy on the client: a circuit breaker always, a retry with exponential backoff
    /// when the client only reads.
    /// </summary>
    private static IHttpClientBuilder WithOutboundPolicy(
        this IHttpClientBuilder builder,
        string clientName,
        OutboundCallKind kind)
    {
        // The breaker is OUTSIDE the retry, so one broken request is one failure for the breaker
        // rather than three: a provider that answers 503 three times in a row has failed, and the
        // next caller should be told at once instead of waiting for another three attempts.
        builder.AddResilienceHandler($"outbound-{clientName}", pipeline =>
        {
            pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                // Polly counts the failures in a window rather than in a row, so the numbers are
                // a share and a floor: the circuit opens when at least half of the calls in the
                // window failed, once there are enough of them for the share to mean anything.
                FailureRatio = CircuitFailureRatio,
                MinimumThroughput = CircuitMinimumThroughput,
                SamplingDuration = CircuitSamplingDuration,
                BreakDuration = CircuitBreakDuration,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .HandleResult(response => IsServerFailure(response.StatusCode)),
            });

            if (kind != OutboundCallKind.Read)
            {
                return;
            }

            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                Delay = RetryDelay,
                MaxDelay = MaxRetryDelay,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .HandleResult(response => IsTransient(response.StatusCode)),
                OnRetry = args =>
                {
                    // The attempt is being replaced and nobody will read this body, but it holds
                    // a connection until it is disposed. The last attempt's response is not
                    // touched here - it is the one the caller gets - so this runs only when a
                    // further attempt is certain.
                    args.Outcome.Result?.Dispose();
                    return default;
                },
            });
        });

        return builder;
    }

    /// <summary>
    /// True for the status codes that mean "ask again": the provider is unwell, not the request.
    /// </summary>
    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    /// <summary>
    /// True for the status codes that mean the provider itself is unwell. A 4xx is the
    /// caller's mistake and says nothing about the provider, so it never opens the circuit.
    /// </summary>
    private static bool IsServerFailure(HttpStatusCode statusCode) =>
        (int)statusCode >= 500;
}

using System.Net;
using FluentAssertions;
using FuelFlow.SharedKernel.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Polly.CircuitBreaker;
using Xunit;

namespace FuelFlow.UnitTests.Resilience;

/// <summary>
/// What the outbound policy actually does (planning finding C-7, issue #537).
/// </summary>
/// <remarks>
/// <para>
/// The policy has two halves and they pull in opposite directions. A client that only reads may be
/// repeated on a transient failure, because reading twice costs nothing. A client that writes may
/// not, because the write is the point: a repeated invoice creation is how one basket becomes two
/// invoices, and a repeated OTP is a second code for one login.
/// </para>
/// <para>
/// These tests are about that difference, and about the circuit breaker, which is the half every
/// client gets: a provider that is down is refused in milliseconds instead of being waited on.
/// </para>
/// </remarks>
public sealed class OutboundResilienceTests
{
    /// <summary>
    /// A read is repeated when the provider is unwell.
    /// </summary>
    [Fact]
    public async Task A_Read_Should_Be_Repeated_When_The_Provider_Is_Unwell()
    {
        var provider = new ScriptedProvider(call => call < 3
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : new HttpResponseMessage(HttpStatusCode.OK));

        using var client = BuildClient(OutboundCallKind.Read, provider);

        using var response = await client.GetAsync("https://provider.test/prices");

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "the provider answered on the third attempt, and the read was repeated until it did");
        provider.Calls.Should().Be(
            3,
            "two failures and then a success is what a transient failure looks like");
    }

    /// <summary>
    /// A write is not repeated, however unwell the provider is.
    /// </summary>
    [Fact]
    public async Task A_Write_Should_Not_Be_Repeated()
    {
        var provider = new ScriptedProvider(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        using var client = BuildClient(OutboundCallKind.Write, provider);

        using var response = await client.PostAsync("https://provider.test/invoice", new StringContent("{}"));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        provider.Calls.Should().Be(
            1,
            "the client writes, so repeating the call could repeat its effect - and the acquirer's "
            + "client creates invoices");
    }

    /// <summary>
    /// A provider that keeps failing is stopped, and the calls after that never reach it.
    /// </summary>
    [Fact]
    public async Task The_Circuit_Should_Open_While_The_Provider_Keeps_Failing()
    {
        var provider = new ScriptedProvider(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        using var client = BuildClient(OutboundCallKind.Write, provider);

        var refused = 0;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            try
            {
                using var response = await client.GetAsync("https://provider.test/prices");
            }
            catch (BrokenCircuitException)
            {
                refused++;
            }
        }

        refused.Should().BeGreaterThan(
            0,
            "a provider that answers 503 to everything is down, and the calls after it opened must "
            + "be refused in milliseconds instead of waiting for the timeout");
        provider.Calls.Should().BeLessThan(
            12,
            "the calls the circuit refused never reached the provider");
    }

    /// <summary>
    /// A client with the provider's deadline, and the provider's script in front of it.
    /// </summary>
    private static HttpClient BuildClient(OutboundCallKind kind, ScriptedProvider provider)
    {
        var services = new ServiceCollection();
        services.AddOutboundHttpClient(
                "probe", TimeSpan.FromSeconds(10), kind)
            .ConfigurePrimaryHttpMessageHandler(() => provider);

        var factory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
        return factory.CreateClient("probe");
    }

    /// <summary>
    /// A provider that answers what the script says and counts what it was asked.
    /// </summary>
    private sealed class ScriptedProvider : HttpMessageHandler
    {
        private readonly Func<int, HttpResponseMessage> _answer;

        public ScriptedProvider(Func<int, HttpResponseMessage> answer)
        {
            _answer = answer;
        }

        /// <summary>How many times the provider was asked.</summary>
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_answer(Calls));
        }
    }
}

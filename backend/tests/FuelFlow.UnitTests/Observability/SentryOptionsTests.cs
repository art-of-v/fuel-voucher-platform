using FluentAssertions;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Configuration;

namespace FuelFlow.UnitTests.Observability;

/// <summary>
/// Guards the "inert without a DSN" contract: Program.cs only calls UseSentry when
/// <see cref="ObservabilityOptions.SentryOptions.IsConfigured"/> is true, so if this
/// gate ever reported configured for a blank DSN the SDK would initialise in every
/// environment (including tests and local dev) and start shipping events.
/// </summary>
public sealed class SentryOptionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsConfigured_IsFalse_WhenDsnBlank(string? dsn)
    {
        new ObservabilityOptions.SentryOptions { Dsn = dsn }
            .IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void IsConfigured_IsTrue_WhenDsnPresent()
    {
        new ObservabilityOptions.SentryOptions { Dsn = "https://abc@o0.ingest.sentry.io/1" }
            .IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void Default_LeavesSentryOff_WithTracingDisabled()
    {
        var options = new ObservabilityOptions();

        options.Sentry.IsConfigured.Should().BeFalse();
        options.Sentry.TracesSampleRatio.Should().Be(0.0);
    }

    [Fact]
    public void BindsFromConfiguration_UnderObservabilitySentrySection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Observability:Sentry:Dsn"] = "https://key@o1.ingest.sentry.io/42",
                ["Observability:Sentry:TracesSampleRatio"] = "0.25"
            })
            .Build();

        var options = configuration
            .GetSection(ObservabilityOptions.SectionName)
            .Get<ObservabilityOptions>()!;

        options.Sentry.IsConfigured.Should().BeTrue();
        options.Sentry.Dsn.Should().Be("https://key@o1.ingest.sentry.io/42");
        options.Sentry.TracesSampleRatio.Should().Be(0.25);
    }
}

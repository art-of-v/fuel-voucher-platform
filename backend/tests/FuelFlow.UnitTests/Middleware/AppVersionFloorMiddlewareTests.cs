using System.Text.Json;
using FluentAssertions;
using FuelFlow.Middleware;
using FuelFlow.SharedKernel.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FuelFlow.UnitTests.Middleware;

/// <summary>
/// The server-side backstop of the update floor: a native client that states a version below the
/// floor is told to update, and the two paths it needs to act on that are left alone.
/// </summary>
public class AppVersionFloorMiddlewareTests
{
    private readonly AppVersionOptions _options = new() { MinimumVersion = "1.2.0" };
    private bool _nextCalled;

    [Fact]
    public async Task A_Native_Client_Below_The_Floor_Is_Told_To_Update()
    {
        var context = Request("/api/brands", "mobile", "1.1.0");

        await Middleware().InvokeAsync(context, Options());

        _nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status426UpgradeRequired);
        Body(context).GetProperty("code").GetString().Should().Be("APP_UPDATE_REQUIRED");
        Body(context).GetProperty("data").GetProperty("minimumVersion").GetString().Should().Be("1.2.0");
    }

    /// <summary>
    /// The refusal is not a sign-out: it carries the same envelope as every other failure, so a
    /// client that reads errors once reads this one too.
    /// </summary>
    [Fact]
    public async Task The_Refusal_Carries_The_Problem_Envelope()
    {
        var context = Request("/api/auth/verify", "mobile", "1.1.9");

        await Middleware().InvokeAsync(context, Options());

        context.Response.ContentType.Should().StartWith("application/problem+json");
        Body(context).GetProperty("status").GetInt32().Should().Be(426);
        Body(context).GetProperty("detail").GetString().Should().Contain("1.2.0");
    }

    [Theory]
    [InlineData("1.2.0")]
    [InlineData("1.2")]
    [InlineData("1.10.0")]
    [InlineData("2.0.0")]
    public async Task A_Client_At_Or_Above_The_Floor_Passes(string version)
    {
        var context = Request("/api/brands", "mobile", version);

        await Middleware().InvokeAsync(context, Options());

        _nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    /// <summary>
    /// Build 236 is exactly this shape: it predates the rollout headers, so it neither claims to be
    /// native nor states a version. Guessing that it is out of date would refuse a client that may
    /// well work - the floor the app polls already covers it.
    /// </summary>
    [Fact]
    public async Task A_Client_That_Does_Not_Say_What_It_Is_Passes()
    {
        var context = Request("/api/brands", platform: null, version: null);

        await Middleware().InvokeAsync(context, Options());

        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task A_Native_Client_That_Does_Not_State_A_Version_Passes()
    {
        var context = Request("/api/brands", "mobile", version: null);

        await Middleware().InvokeAsync(context, Options());

        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task A_Version_That_Does_Not_Parse_Passes()
    {
        var context = Request("/api/brands", "mobile", "1.2.0 (build 237)");

        await Middleware().InvokeAsync(context, Options());

        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task A_Browser_With_An_Old_Version_Header_Passes()
    {
        var context = Request("/api/brands", "web", "1.1.0");

        await Middleware().InvokeAsync(context, Options());

        _nextCalled.Should().BeTrue();
    }

    /// <summary>
    /// The floor the client polls is how it finds out it is out of date, and the update channel is
    /// how a fleet without a store visit recovers. Refusing either would leave the client with
    /// nothing to do about the refusal.
    /// </summary>
    [Theory]
    [InlineData("/api/app-version")]
    [InlineData("/api/updates/manifest")]
    [InlineData("/health/live")]
    [InlineData("/health")]
    [InlineData("/metrics")]
    public async Task The_Paths_The_Client_Needs_To_Recover_Stay_Open(string path)
    {
        var context = Request(path, "mobile", "1.0.0");

        await Middleware().InvokeAsync(context, Options());

        _nextCalled.Should().BeTrue();
    }

    /// <summary>
    /// The exemption is by path segment, not by prefix: an endpoint that merely starts with the
    /// letters of an exempt path is not the exempt endpoint.
    /// </summary>
    [Fact]
    public async Task A_Path_That_Merely_Starts_Like_An_Exempt_One_Is_Not_Exempt()
    {
        var context = Request("/healthcheck-admin", "mobile", "1.0.0");

        await Middleware().InvokeAsync(context, Options());

        _nextCalled.Should().BeFalse();
    }

    private AppVersionFloorMiddleware Middleware() => new(
        _ =>
        {
            _nextCalled = true;
            return Task.CompletedTask;
        },
        NullLogger<AppVersionFloorMiddleware>.Instance);

    private IOptionsMonitor<AppVersionOptions> Options()
    {
        var monitor = new Mock<IOptionsMonitor<AppVersionOptions>>();
        monitor.SetupGet(value => value.CurrentValue).Returns(_options);
        return monitor.Object;
    }

    private static DefaultHttpContext Request(string path, string? platform, string? version)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        if (platform is not null)
            context.Request.Headers[AttestationMiddleware.ClientPlatformHeader] = platform;

        if (version is not null)
            context.Request.Headers[AttestationMiddleware.AppVersionHeader] = version;

        return context;
    }

    private static JsonElement Body(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var document = JsonDocument.Parse(context.Response.Body);
        return document.RootElement.Clone();
    }
}

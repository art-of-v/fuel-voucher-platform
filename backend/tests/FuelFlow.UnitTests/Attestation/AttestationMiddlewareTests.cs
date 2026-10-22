using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.Features.Attestation;
using FuelFlow.Middleware;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Security.Attestation;
using FuelFlow.SharedKernel.Services;
using FuelFlow.SharedKernel.Time;
using FuelFlow.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace FuelFlow.UnitTests.Attestation;

public sealed class AttestationMiddlewareTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly TestTimeProvider _time = new(TestClock.Now);
    private readonly Mock<IAppleAppAttestVerifier> _apple = new();
    private readonly Mock<IPlayIntegrityVerifier> _google = new();
    private readonly AttestationOptions _settings = new()
    {
        Enabled = true,
        Mode = AttestationMode.Enforce,
        ChallengeExpirySeconds = 120,
        MinimumEnforcedAppVersion = "1.2.0",
        EnforcePlatforms = ["ios"],
        AllowDevelopmentBypass = false,
    };

    private bool _nextCalled;

    public AttestationMiddlewareTests()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new ApplicationDbContext(dbOptions, TestQrCipher.Instance);
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    [Fact]
    public async Task MissingProof_IsRejected_WhenTheRequestIsInTheEnforcedRollout()
    {
        var context = CreateRequest("/api/purchases", "{}");
        AddRolloutHeaders(context, "ios", "1.2.0");

        await CreateMiddleware().InvokeAsync(
            context,
            CreateService(),
            CreateOptions(),
            CreateEnvironment());

        _nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        context.Response.Headers["X-Attestation-Error"].ToString().Should().Be("ATTESTATION_REQUIRED");
        ReadCode(context).Should().Be("ATTESTATION_REQUIRED");
    }

    [Fact]
    public async Task MissingProof_Continues_ForAnOldBuild()
    {
        var context = CreateRequest("/api/purchases", "{}");
        AddRolloutHeaders(context, "ios", "1.1.9");

        await CreateMiddleware().InvokeAsync(
            context,
            CreateService(),
            CreateOptions(),
            CreateEnvironment());

        _nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task MissingProof_Continues_ForANonMobileCaller()
    {
        var context = CreateRequest("/api/purchases", "{}");
        context.Request.Headers[AttestationMiddleware.ClientPlatformHeader] = "web";

        await CreateMiddleware().InvokeAsync(
            context,
            CreateService(),
            CreateOptions(),
            CreateEnvironment());

        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task MissingProof_Continues_WhenModeIsReportOnly()
    {
        _settings.Mode = AttestationMode.ReportOnly;

        var context = CreateRequest("/api/purchases", "{}");
        AddRolloutHeaders(context, "ios", "1.2.0");

        await CreateMiddleware().InvokeAsync(
            context,
            CreateService(),
            CreateOptions(),
            CreateEnvironment());

        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task MissingProof_Continues_OnTheDevelopmentBypass()
    {
        _settings.AllowDevelopmentBypass = true;
        var context = CreateRequest("/api/purchases", "{}");
        AddRolloutHeaders(context, "ios", "1.2.0");

        await CreateMiddleware().InvokeAsync(
            context,
            CreateService(),
            CreateOptions(),
            CreateEnvironment("Development"));

        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task UnprotectedPath_IsNotChecked()
    {
        var context = CreateRequest("/api/health", "{}");
        AddRolloutHeaders(context, "ios", "1.2.0");

        await CreateMiddleware().InvokeAsync(
            context,
            CreateService(),
            CreateOptions(),
            CreateEnvironment());

        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvalidProof_IsRejected_ButTheRequestBodyIsStillReadable()
    {
        const string body = "{\"amount\":10}";
        var context = CreateRequest("/api/purchases", body);
        AddRolloutHeaders(context, "ios", "1.2.0");
        context.Request.Headers[AttestationMiddleware.ProviderHeader] = "apple";
        context.Request.Headers[AttestationMiddleware.ChallengeHeader] = "challenge";
        context.Request.Headers[AttestationMiddleware.ProofHeader] = Convert.ToBase64String(
            RandomNumberGenerator.GetBytes(32));
        context.Request.Headers[AttestationMiddleware.KeyIdHeader] = Convert.ToBase64String(
            RandomNumberGenerator.GetBytes(32));

        await CreateMiddleware().InvokeAsync(
            context,
            CreateService(),
            CreateOptions(),
            CreateEnvironment());

        _nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        context.Response.Headers["X-Attestation-Error"].ToString().Should().Be("ATTESTATION_CHALLENGE_INVALID");
        ReadCode(context).Should().Be("ATTESTATION_CHALLENGE_INVALID");

        context.Request.Body.Position = 0;
        using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
        (await reader.ReadToEndAsync()).Should().Be(body);
    }

    [Fact]
    public async Task UnavailableChallengeStore_IsRejectedWith503()
    {
        _settings.EnforcePlatforms = ["ios", "android"];

        var cache = new Mock<ICacheService>();
        cache
            .Setup(store => store.GetAndDeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cache is down"));

        var context = CreateRequest("/api/purchases", "{}");
        AddRolloutHeaders(context, "android", "1.2.0");
        context.Request.Headers[AttestationMiddleware.ProviderHeader] = "google";
        context.Request.Headers[AttestationMiddleware.ChallengeHeader] = "challenge";
        context.Request.Headers[AttestationMiddleware.ProofHeader] = "token";

        await CreateMiddleware().InvokeAsync(
            context,
            CreateService(cache.Object),
            CreateOptions(),
            CreateEnvironment());

        _nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        ReadCode(context).Should().Be("ATTESTATION_UNAVAILABLE");
    }

    private AttestationMiddleware CreateMiddleware() => new(
        _ =>
        {
            _nextCalled = true;
            return Task.CompletedTask;
        },
        NullLogger<AttestationMiddleware>.Instance);

    private AttestationService CreateService(ICacheService? cache = null)
    {
        var challenges = new AttestationChallengeService(
            cache ?? new InMemoryCacheService(NullLogger<InMemoryCacheService>.Instance, _time),
            CreateOptions(),
            _time,
            NullLogger<AttestationChallengeService>.Instance);

        return new AttestationService(
            _db,
            challenges,
            _apple.Object,
            _google.Object,
            _time,
            NullLogger<AttestationService>.Instance);
    }

    private IOptionsMonitor<AttestationOptions> CreateOptions()
    {
        var monitor = new Mock<IOptionsMonitor<AttestationOptions>>();
        monitor.SetupGet(value => value.CurrentValue).Returns(_settings);
        return monitor.Object;
    }

    private static IWebHostEnvironment CreateEnvironment(string name = "Production")
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(value => value.EnvironmentName).Returns(name);
        return environment.Object;
    }

    private static DefaultHttpContext CreateRequest(string path, string body)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = path;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Request.ContentLength = Encoding.UTF8.GetByteCount(body);
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static void AddRolloutHeaders(HttpContext context, string os, string version)
    {
        context.Request.Headers[AttestationMiddleware.ClientPlatformHeader] = "mobile";
        context.Request.Headers[AttestationMiddleware.ClientOsHeader] = os;
        context.Request.Headers[AttestationMiddleware.AppVersionHeader] = version;
        context.Request.Headers[AttestationMiddleware.DeviceIdHeader] = "device-1";
    }

    private static string? ReadCode(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var document = JsonDocument.Parse(context.Response.Body);
        return document.RootElement.TryGetProperty("code", out var code)
            ? code.GetString()
            : null;
    }
}

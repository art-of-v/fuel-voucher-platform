using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Updates;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Options;
using FuelFlow.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FuelFlow.UnitTests.Updates;

public class UpdatesControllerTests
{
    private const string BaseUrl = "https://ota.example";
    private const string Signature = "sig=\"abc123\", keyid=\"main\", alg=\"rsa-v1_5-sha256\"";

    // The controller now also records what it served (F-13); a fresh in-memory database per
    // controller keeps that record readable from the test that asserts it.
    private ApplicationDbContext? _context;

    [Fact]
    public async Task GetManifest_WithPublishedUpdate_ReturnsSignedMultipartBody()
    {
        const string manifestJson = "{\"id\":\"update-1\",\"runtimeVersion\":\"1.0.0\"}";
        var (controller, _, _) = CreateController(HttpStatusCode.OK, DescriptorJson(manifestJson, Signature), BaseUrl);

        var result = await controller.GetManifest(CancellationToken.None);

        var file = result.Should().BeOfType<FileContentResult>().Subject;
        file.ContentType.Should().StartWith("multipart/mixed; boundary=\"");

        var body = Encoding.UTF8.GetString(file.FileContents);
        body.Should().Contain("name=\"manifest\"");
        body.Should().Contain("expo-signature: " + Signature);
        body.Should().Contain(manifestJson);

        controller.Response.Headers["expo-protocol-version"].ToString().Should().Be("1");
        controller.Response.Headers["expo-sfv-version"].ToString().Should().Be("0");
        controller.Response.Headers.CacheControl.ToString().Should().Contain("private");
    }

    [Fact]
    public async Task GetManifest_PreservesSignedManifestBytesVerbatim()
    {
        // A signature is computed over exact bytes; re-serialising the JSON would change whitespace and
        // break verification. The part body must therefore reproduce the decoded manifest byte-for-byte,
        // including the deliberately irregular spacing below.
        var manifestBytes = Encoding.UTF8.GetBytes("{ \"id\" :\"update-2\",  \"extra\" : 1 }");
        var descriptor = JsonSerializer.Serialize(new
        {
            manifestBase64 = Convert.ToBase64String(manifestBytes),
            signature = Signature,
        });
        var (controller, _, _) = CreateController(HttpStatusCode.OK, descriptor, BaseUrl);

        var result = await controller.GetManifest(CancellationToken.None);

        var file = result.Should().BeOfType<FileContentResult>().Subject;
        Encoding.UTF8.GetString(file.FileContents).Should().Contain(Encoding.UTF8.GetString(manifestBytes));
    }

    [Fact]
    public async Task GetManifest_RequestsContentAddressedR2Key()
    {
        var (controller, handler, _) = CreateController(HttpStatusCode.NotFound, "", BaseUrl,
            platform: "ios", runtimeVersion: "1.0.0");

        await controller.GetManifest(CancellationToken.None);

        handler.Request!.RequestUri!.ToString().Should().Be("https://ota.example/ios/1.0.0/update.json");
    }

    [Fact]
    public async Task GetManifest_WhenNothingPublished_Returns204()
    {
        var (controller, _, _) = CreateController(HttpStatusCode.NotFound, "", BaseUrl);

        var result = await controller.GetManifest(CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        controller.Response.Headers["expo-protocol-version"].ToString().Should().Be("1");
    }

    [Fact]
    public async Task GetManifest_WhenNotConfigured_Returns204WithoutCallingUpstream()
    {
        var (controller, handler, _) = CreateController(HttpStatusCode.OK, DescriptorJson("{}", Signature),
            publicBaseUrl: "");

        var result = await controller.GetManifest(CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        handler.Request.Should().BeNull("an unconfigured endpoint must not hit R2");
    }

    [Fact]
    public async Task GetManifest_WhenUpstreamFails_Returns502()
    {
        var (controller, _, _) = CreateController(HttpStatusCode.InternalServerError, "boom", BaseUrl);

        var result = await controller.GetManifest(CancellationToken.None);

        result.Should().BeOfType<StatusCodeResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status502BadGateway);
    }

    [Fact]
    public async Task GetManifest_WhenDescriptorMalformed_Returns502()
    {
        // 200 with a well-formed descriptor whose manifestBase64 is not valid base64 -> the decode
        // inside the controller's try throws -> 502, never an unhandled 500.
        var descriptor = JsonSerializer.Serialize(new
        {
            manifestBase64 = "!!!not-base64!!!",
            signature = Signature,
        });
        var (controller, _, _) = CreateController(HttpStatusCode.OK, descriptor, BaseUrl);

        var result = await controller.GetManifest(CancellationToken.None);

        result.Should().BeOfType<StatusCodeResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status502BadGateway);
    }

    [Theory]
    [InlineData("", "1.0.0")]
    [InlineData("ios", "")]
    public async Task GetManifest_WithMissingHeaders_Returns400(string platform, string runtimeVersion)
    {
        var (controller, _, _) = CreateController(HttpStatusCode.OK, DescriptorJson("{}", Signature), BaseUrl,
            platform: platform, runtimeVersion: runtimeVersion);

        var result = await controller.GetManifest(CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>
    /// The runtime version in the log is the only record that an out-of-date build is still polling: the
    /// endpoint answers an unknown fingerprint with the same 204 as an up-to-date client, and the devices
    /// table stores the marketing version rather than the fingerprint. If this log ever stops carrying
    /// the pair, "which builds exist in the wild" becomes unanswerable again - and so does the decision of
    /// whether a legacy publish is worth doing.
    /// </summary>
    [Fact]
    public async Task GetManifest_LogsTheRuntimeVersionItWasAskedFor()
    {
        var (controller, _, logger) = CreateController(HttpStatusCode.NotFound, "", BaseUrl,
            platform: "ios", runtimeVersion: "0e1f2a3b-unreachable-legacy-fingerprint");

        await controller.GetManifest(CancellationToken.None);

        logger.Entries.Should().ContainSingle()
            .Which.Message.Should().Contain("0e1f2a3b-unreachable-legacy-fingerprint");
    }

    [Theory]
    [InlineData("", "1.0.0", false, true)]
    [InlineData("ios", "", true, false)]
    public async Task GetManifest_WithMissingHeaders_WarnsAndNamesWhatWasMissing(
        string platform, string runtimeVersion, bool expectPlatform, bool expectRuntimeVersion)
    {
        var (controller, _, logger) = CreateController(HttpStatusCode.OK, DescriptorJson("{}", Signature), BaseUrl,
            platform: platform, runtimeVersion: runtimeVersion);

        await controller.GetManifest(CancellationToken.None);

        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning, "a client asking without the headers is a misconfiguration, not a no-op");
        entry.Fields.Should().Contain("HasPlatform", expectPlatform);
        entry.Fields.Should().Contain("HasRuntimeVersion", expectRuntimeVersion);
    }

    /// <summary>
    /// The serving side cannot notice a rollback for a channel it never recorded, so the recording
    /// has to happen on the request that delivers the update - not in a background job the endpoint
    /// does not wait for, and not only when something looks unusual.
    /// </summary>
    [Fact]
    public async Task GetManifest_RecordsTheManifestItServes()
    {
        const string manifestJson =
            "{\"id\":\"update-9\",\"metadata\":{\"commit\":\"abc123\",\"committedAt\":\"2026-10-30T10:00:00+00:00\"}}";
        var (controller, _, _) = CreateController(HttpStatusCode.OK, DescriptorJson(manifestJson, Signature), BaseUrl);

        await controller.GetManifest(CancellationToken.None);

        var key = ServedManifestTracker.SettingKeyPrefix + "ios/1.0.0";
        var row = await _context!.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key);
        row.Should().NotBeNull();
        row!.Value.Should().Contain("abc123").And.Contain("2026-10-30");
    }

    private static string DescriptorJson(string manifestJson, string signature)
    {
        var manifestBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(manifestJson));
        return JsonSerializer.Serialize(new { manifestBase64, signature });
    }

    private (UpdatesController Controller, RecordingHttpMessageHandler Handler, CapturingLogger Logger)
        CreateController(
            HttpStatusCode statusCode,
            string responseBody,
            string publicBaseUrl,
            string platform = "ios",
            string runtimeVersion = "1.0.0")
    {
        var handler = new RecordingHttpMessageHandler
        {
            Responder = _ => new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            }
        };
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new UpdatesOptions { PublicBaseUrl = publicBaseUrl });
        var logger = new CapturingLogger();

        _context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            TestQrCipher.Instance);

        var tracker = new ServedManifestTracker(
            new RuntimeSettingsService(_context), new FuelFlowMetrics(),
            NullLogger<ServedManifestTracker>.Instance);

        var controller = new UpdatesController(
            new UpdateManifestSource(httpClient, options), tracker, logger)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        if (!string.IsNullOrEmpty(platform))
            controller.Request.Headers["expo-platform"] = platform;
        if (!string.IsNullOrEmpty(runtimeVersion))
            controller.Request.Headers["expo-runtime-version"] = runtimeVersion;

        return (controller, handler, logger);
    }

    /// <summary>
    /// Captures what the controller logged, with the structured fields kept as fields - the point of the
    /// change under test is that <c>RuntimeVersion</c> arrives as a queryable field in Loki, not only as
    /// text inside a formatted sentence.
    /// </summary>
    private sealed class CapturingLogger : ILogger<UpdatesController>
    {
        public List<(LogLevel Level, string Message, Dictionary<string, object?> Fields)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            // The framework hands over FormattedLogValues, which is a list of key/value pairs - not a
            // dictionary - so the cast that would read most naturally here is always null.
            var pairs = state as IEnumerable<KeyValuePair<string, object?>>;
            var fields = pairs is null
                ? new Dictionary<string, object?>()
                : pairs.ToDictionary(p => p.Key, p => p.Value);

            Entries.Add((logLevel, formatter(state, exception), fields));
        }
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } = null!;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(Responder(request));
        }
    }
}

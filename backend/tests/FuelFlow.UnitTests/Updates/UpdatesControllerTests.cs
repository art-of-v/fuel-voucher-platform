using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.Features.Updates;
using FuelFlow.SharedKernel.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FuelFlow.UnitTests.Updates;

public class UpdatesControllerTests
{
    private const string BaseUrl = "https://ota.example";
    private const string Signature = "sig=\"abc123\", keyid=\"main\", alg=\"rsa-v1_5-sha256\"";

    [Fact]
    public async Task GetManifest_WithPublishedUpdate_ReturnsSignedMultipartBody()
    {
        const string manifestJson = "{\"id\":\"update-1\",\"runtimeVersion\":\"1.0.0\"}";
        var (controller, _) = CreateController(HttpStatusCode.OK, DescriptorJson(manifestJson, Signature), BaseUrl);

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
        var (controller, _) = CreateController(HttpStatusCode.OK, descriptor, BaseUrl);

        var result = await controller.GetManifest(CancellationToken.None);

        var file = result.Should().BeOfType<FileContentResult>().Subject;
        Encoding.UTF8.GetString(file.FileContents).Should().Contain(Encoding.UTF8.GetString(manifestBytes));
    }

    [Fact]
    public async Task GetManifest_RequestsContentAddressedR2Key()
    {
        var (controller, handler) = CreateController(HttpStatusCode.NotFound, "", BaseUrl,
            platform: "ios", runtimeVersion: "1.0.0");

        await controller.GetManifest(CancellationToken.None);

        handler.Request!.RequestUri!.ToString().Should().Be("https://ota.example/ios/1.0.0/update.json");
    }

    [Fact]
    public async Task GetManifest_WhenNothingPublished_Returns204()
    {
        var (controller, _) = CreateController(HttpStatusCode.NotFound, "", BaseUrl);

        var result = await controller.GetManifest(CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        controller.Response.Headers["expo-protocol-version"].ToString().Should().Be("1");
    }

    [Fact]
    public async Task GetManifest_WhenNotConfigured_Returns204WithoutCallingUpstream()
    {
        var (controller, handler) = CreateController(HttpStatusCode.OK, DescriptorJson("{}", Signature),
            publicBaseUrl: "");

        var result = await controller.GetManifest(CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        handler.Request.Should().BeNull("an unconfigured endpoint must not hit R2");
    }

    [Fact]
    public async Task GetManifest_WhenUpstreamFails_Returns502()
    {
        var (controller, _) = CreateController(HttpStatusCode.InternalServerError, "boom", BaseUrl);

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
        var (controller, _) = CreateController(HttpStatusCode.OK, descriptor, BaseUrl);

        var result = await controller.GetManifest(CancellationToken.None);

        result.Should().BeOfType<StatusCodeResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status502BadGateway);
    }

    [Theory]
    [InlineData("", "1.0.0")]
    [InlineData("ios", "")]
    public async Task GetManifest_WithMissingHeaders_Returns400(string platform, string runtimeVersion)
    {
        var (controller, _) = CreateController(HttpStatusCode.OK, DescriptorJson("{}", Signature), BaseUrl,
            platform: platform, runtimeVersion: runtimeVersion);

        var result = await controller.GetManifest(CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    private static string DescriptorJson(string manifestJson, string signature)
    {
        var manifestBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(manifestJson));
        return JsonSerializer.Serialize(new { manifestBase64, signature });
    }

    private static (UpdatesController Controller, RecordingHttpMessageHandler Handler) CreateController(
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
        var source = new UpdateManifestSource(httpClient, options);

        var controller = new UpdatesController(source, NullLogger<UpdatesController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        if (!string.IsNullOrEmpty(platform))
            controller.Request.Headers["expo-platform"] = platform;
        if (!string.IsNullOrEmpty(runtimeVersion))
            controller.Request.Headers["expo-runtime-version"] = runtimeVersion;

        return (controller, handler);
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

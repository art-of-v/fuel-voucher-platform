using System.Text;
using FluentAssertions;
using FuelFlow.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FuelFlow.UnitTests.Middleware;

/// <summary>
/// The request-body ceiling, exercised on the middleware that enforces it.
/// </summary>
/// <remarks>
/// The ceiling is enforced by the application rather than by the web server alone, so that it
/// holds in every host the API runs in - including the test host, which has no Kestrel limits at
/// all. These tests are the proof that it does.
/// </remarks>
public sealed class RequestSizeLimitMiddlewareTests
{
    [Fact]
    public async Task A_Body_Over_The_Ceiling_Is_Refused_With_413()
    {
        var context = Context(bodyLength: RequestSizeLimits.DefaultMaxRequestBodySize + 1);

        await InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);
        (await BodyOf(context)).Should().Contain("too large");
    }

    [Fact]
    public async Task A_Body_Within_The_Ceiling_Reaches_The_Endpoint()
    {
        var context = Context(bodyLength: 1024);
        var reached = false;
        var middleware = new RequestSizeLimitMiddleware(
            _ =>
            {
                reached = true;
                return Task.CompletedTask;
            },
            NullLogger<RequestSizeLimitMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        reached.Should().BeTrue("a body within the ceiling is the endpoint's business");
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task A_Route_That_Raises_The_Ceiling_Accepts_A_Bigger_Body()
    {
        // The voucher-PDF import is the route the 25 MB ceiling exists for: it declares its own
        // limit, and the middleware must honour that declaration rather than the global one.
        var bodyLength = RequestSizeLimits.DefaultMaxRequestBodySize + 1;
        var context = Context(bodyLength: bodyLength, declaredLimit: RequestSizeLimits.FileUploadMaxRequestBodySize);
        var reached = false;
        var middleware = new RequestSizeLimitMiddleware(
            _ =>
            {
                reached = true;
                return Task.CompletedTask;
            },
            NullLogger<RequestSizeLimitMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        reached.Should().BeTrue("the route raised the ceiling for itself");
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task A_Chunked_Body_Over_The_Ceiling_Is_Refused_While_It_Is_Read()
    {
        // A body that does not declare its length cannot be refused before it is read, so the
        // stream counts the bytes as they go past and refuses the moment the count is over.
        var context = Context(bodyLength: RequestSizeLimits.DefaultMaxRequestBodySize + 1, declareLength: false);
        var middleware = new RequestSizeLimitMiddleware(
            async ctx =>
            {
                await ctx.Request.Body.CopyToAsync(Stream.Null);
            },
            NullLogger<RequestSizeLimitMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);
    }

    private static async Task InvokeAsync(DefaultHttpContext context)
    {
        var middleware = new RequestSizeLimitMiddleware(
            _ => Task.CompletedTask,
            NullLogger<RequestSizeLimitMiddleware>.Instance);

        await middleware.InvokeAsync(context);
    }

    private static async Task<string> BodyOf(DefaultHttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    /// <summary>
    /// A request with a body of the given length, on an endpoint that declares the given limit
    /// (or on no limit at all when none is declared).
    /// </summary>
    private static DefaultHttpContext Context(long bodyLength, long? declaredLimit = null, bool declareLength = true)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider()
        };

        context.Request.Method = "POST";
        context.Request.Path = "/api/test";
        context.Request.Body = new MemoryStream(new byte[bodyLength]);
        if (declareLength)
        {
            context.Request.ContentLength = bodyLength;
        }

        context.Response.Body = new MemoryStream();

        var metadata = declaredLimit is { } limit
            ? new EndpointMetadataCollection(new IRequestSizeLimitMetadata[] { new RequestSizeLimitAttribute(limit) })
            : new EndpointMetadataCollection(Array.Empty<IRequestSizeLimitMetadata>());

        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, metadata, "test"));

        return context;
    }
}

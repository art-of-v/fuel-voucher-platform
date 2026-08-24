using FluentAssertions;

namespace FuelFlow.UnitTests.Middleware;

/// <summary>
/// Pins the middleware ordering in <c>FuelFlow.API/Extensions/PipelineSetup.cs</c>.
///
/// <para>
/// Pipeline order is not observable through ASP.NET Core's public surface once built, so the
/// only regression guard that survives an innocent-looking refactor of UseAppPipeline is a
/// convention test over its source. The orderings below are load-bearing:
/// </para>
///
/// <list type="bullet">
/// <item>ForwardedHeaders before everything - every downstream consumer (logging, rate-limit
/// partitions, auth audits) must resolve the real client IP, or all clients collapse into one
/// partition (FF-19) or can spoof their own.</item>
/// <item>UseAuthentication before UseRateLimiter - the named policies partition per user where
/// a user exists; with the limiter mounted first, HttpContext.User is unpopulated when
/// partition keys are computed, silently turning every per-user limit into a per-IP one
/// without any test failing.</item>
/// <item>UseAuthorization after UseRateLimiter - a rejected request should not have spent
/// authorization work, and the fallback policy's 401s must still pass through the limiter to
/// stay cost-bounded.</item>
/// </list>
/// </summary>
public sealed class PipelineOrderConventionTests
{
    private const string SourceRelativePath = "src/FuelFlow.API/Extensions/PipelineSetup.cs";

    private static string ReadPipelineSource()
    {
        // Walk up from the test output directory until the source file appears; counting fixed
        // levels is brittle because AppContext.BaseDirectory keeps its trailing separator.
        var candidate = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && candidate is not null; i++, candidate = Path.GetDirectoryName(candidate))
        {
            var path = Path.Combine(candidate, SourceRelativePath);
            if (File.Exists(path))
                return File.ReadAllText(path);
        }

        throw new FileNotFoundException(
            $"Could not locate {SourceRelativePath} walking up from {AppContext.BaseDirectory}; if the layout moved, update SourceRelativePath");
    }

    [Theory]
    [InlineData("app.UseForwardedHeaders()", "app.UseMiddleware<RequestLoggingMiddleware>()")]
    [InlineData("app.UseForwardedHeaders()", "app.UseAuthentication()")]
    [InlineData("app.UseForwardedHeaders()", "app.UseRateLimiter()")]
    [InlineData("app.UseAuthentication()", "app.UseRateLimiter()")]
    [InlineData("app.UseAuthentication()", "app.UseAuthorization()")]
    [InlineData("app.UseRateLimiter()", "app.UseAuthorization()")]
    public void Pipeline_MountsSecurityMiddlewares_InLoadBearingOrder(string before, string after)
    {
        var source = ReadPipelineSource();

        var beforeIndex = source.IndexOf(before, StringComparison.Ordinal);
        var afterIndex = source.IndexOf(after, StringComparison.Ordinal);

        beforeIndex.Should().BeGreaterThanOrEqualTo(0, "'{0}' should exist in PipelineSetup.cs", before);
        afterIndex.Should().BeGreaterThanOrEqualTo(0, "'{0}' should exist in PipelineSetup.cs", after);
        beforeIndex.Should().BeLessThan(afterIndex,
            "{0} must be mounted before {1}; reordering these changes security behaviour, " +
            "not just structure - see this test's summary", before, after);
    }
}

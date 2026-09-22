using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Pins the set of endpoints reachable without a token.
///
/// <para>
/// AuthSetup installs a <c>RequireAuthenticatedUser</c> fallback policy, so authorization is now
/// opt-out rather than opt-in. That closes the FF-01 class of bug - a feature slice that forgets
/// <c>[Authorize]</c> and becomes anonymous - but it introduces the mirror-image risk: a public
/// endpoint that forgets <c>[AllowAnonymous]</c> answers 401 in production. For the station list
/// that is a visible outage; for the Monobank webhook it is silently lost payment callbacks, which
/// means the customer is charged and no vouchers are issued.
/// </para>
/// <para>
/// Both directions are therefore asserted here against the real composition root: the anonymous set
/// must equal this list exactly. Adding an endpoint to it requires editing this file, which makes
/// every future change to the public attack surface a reviewed decision rather than a side effect
/// of an attribute someone did or did not type.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public class AnonymousEndpointSurfaceTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public AnonymousEndpointSurfaceTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Every endpoint intended to be reachable without a bearer token, as "METHOD path".
    /// Route templates are compared lowercased because MVC's <c>[controller]</c> token yields the
    /// class-cased segment (<c>api/Auth</c>) while routing itself is case-insensitive - matching on
    /// the casing would make this test fail on a rename that changes nothing about reachability.
    /// </summary>
    private static readonly string[] ExpectedAnonymousEndpoints =
    [
        // Liveness/readiness probe and the mobile force-upgrade check. Both must answer before a
        // client has any credentials at all. HEAD is for uptime monitors (UptimeRobot's free tier
        // probes with HEAD). /health/live and /health/ready are for orchestration (K8s, LB).
        "GET /health",
        "HEAD /health",
        "GET /health/live",
        // MapHealthChecks and MapPrometheusScrapingEndpoint register with no HTTP-method
        // constraint (unlike /health above, which is mapped [Get, Head]), so routing answers
        // them for every verb and Describe() emits "* path". Both are idempotent reads -
        // readiness re-runs the same probe, /metrics returns the same scrape and is blocked
        // at the public edge by Caddy - so the extra verbs are not additional attack surface.
        "* /health/ready",
        "* /metrics",
        "GET /api/app-version",

        // OTP login. Rate-limited per phone and per IP (send-code, verify-code, refresh policies).
        "POST /api/auth/send-code",
        "POST /api/auth/verify",
        // Admin-panel login. Same wire shape, but authorizes (staff-only) before sending a code
        // and never auto-registers. Anonymous for the same reason as send-code/verify: the caller
        // has no token yet. Non-staff callers are rejected inside the handler, not by [Authorize].
        "POST /api/auth/admin/send-code",
        "POST /api/auth/admin/verify",
        "POST /api/auth/refresh",
        // Session logout: clears the httpOnly refresh cookie and revokes its token.
        // Path is under /refresh so the cookie (scoped to /api/auth/refresh) is sent
        // here. Anonymous because it authenticates by that cookie, not a bearer token,
        // and must still clear a stale cookie after the access token expires.
        "POST /api/auth/refresh/logout",

        // Device binding: the challenge/response handshake happens before the device holds a token.
        // verify-raw is a key-format diagnostic that returns 404 unless Auth:DevBypass is set, and
        // ValidateSecurityConfiguration refuses to boot Production with DevBypass on.
        "POST /api/auth/device/challenge",
        "POST /api/auth/device/verify",
        "POST /api/auth/device/verify-raw",

        // Payment callback. Authenticates by ECDSA signature over the raw body, not by token.
        "POST /api/monobank/webhook",

        // Verified email-change confirmation. Reached from the link emailed to the NEW address;
        // possession of the one-time token (looked up by hash, 24h expiry) is the proof, so it is
        // intentionally anonymous - a signed-in session is neither available nor required here.
        "GET /api/auth/email/confirm",

        // Public catalogue: the store front has to be browsable before sign-in. All of these
        // project explicit DTOs rather than returning entities, so a column added to a table
        // cannot become public reference data by accident (FF-31).
        "GET /api/packages",
        "GET /api/packages/station/{stationid}",
        "GET /api/stations",
        "GET /api/stations/fuel-types",
        "GET /api/station-nodes",
        "GET /api/station-nodes/station/{stationid}",

        // Public contact form behind the /support page on palne.shop: a visitor with no account
        // must be able to reach support. Anonymous by design and spam-hardened at the endpoint
        // (per-IP named rate limit + global limiter + a "website" honeypot answered 204), see
        // SupportController. A reviewed decision, not a missing [Authorize].
        "POST /api/support/messages",
    ];

    [Fact]
    public async Task FallbackPolicy_RequiresAnAuthenticatedUser()
    {
        // Without this the test below still passes while the fallback has been deleted: every
        // endpoint that never carried an [Authorize] attribute would quietly go back to anonymous
        // without appearing in the IAllowAnonymous set.
        using var scope = _fixture.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();

        var fallback = await provider.GetFallbackPolicyAsync();

        fallback.Should().NotBeNull("authorization must be opt-out, not opt-in");
        fallback!.Requirements.Should().ContainSingle(r => r is DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public void AnonymousSurface_IsExactlyTheReviewedSet()
    {
        var endpoints = RoutedEndpoints();

        // Guard against a silently empty or partial endpoint graph making the comparison
        // vacuous - a false "no anonymous endpoints" would otherwise look like a pass.
        endpoints.Should().HaveCountGreaterThan(100,
            "the whole routed endpoint graph should be visible to this test");

        var actual = endpoints
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .SelectMany(Describe)
            .Distinct()
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        actual.Should().BeEquivalentTo(ExpectedAnonymousEndpoints,
            "an endpoint reachable without a token is an attack-surface decision; add it to " +
            nameof(ExpectedAnonymousEndpoints) + " deliberately, or attribute it with [Authorize]");
    }

    /// <summary>
    /// With the fallback policy in place, an endpoint carrying neither <c>[Authorize]</c> nor
    /// <c>[AllowAnonymous]</c> requires authentication. Such an endpoint is still worth failing on:
    /// its reachability is then decided by a global setting somewhere else rather than by anything
    /// visible at the endpoint, which is how FF-01 happened.
    /// </summary>
    [Fact]
    public void EveryEndpoint_StatesItsOwnAuthorizationIntent()
    {
        var undeclared = RoutedEndpoints()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null
                     && e.Metadata.GetMetadata<IAuthorizeData>() is null)
            .SelectMany(Describe)
            .Distinct()
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        undeclared.Should().BeEmpty(
            "each endpoint should carry [Authorize] or [AllowAnonymous] at the action or controller " +
            "level so its reachability is readable where it is defined");
    }

    private IReadOnlyList<RouteEndpoint> RoutedEndpoints()
    {
        // The composite EndpointDataSource is assembled by UseEndpoints, so ask the root provider
        // for every registered source and flatten. CreateClient() forces the host - and therefore
        // the endpoint graph - to be built before this runs.
        _ = _fixture.CreateClient();

        return _fixture.Services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
    }

    private static IEnumerable<string> Describe(RouteEndpoint endpoint)
    {
        var path = "/" + endpoint.RoutePattern.RawText?.TrimStart('/').ToLowerInvariant();

        var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
        if (methods is null || methods.Count == 0)
            return [$"* {path}"];

        return methods.Select(m => $"{m.ToUpperInvariant()} {path}");
    }
}

using FluentAssertions;
using FuelFlow.Features.Auth.EmailChange;

namespace FuelFlow.UnitTests.Auth;

/// <summary>
/// Covers the confirmation-link origin resolution behind planning #48: an admin-initiated email
/// change built the confirm link from the request host (app.palne.shop), whose reverse proxy 404s
/// /api/auth/email/confirm (that path is not on the admin origin's /api/* allow-list). The fix
/// resolves a configured canonical API origin instead, falling back to the request host only when
/// unconfigured (dev/local, single origin).
/// </summary>
public sealed class PendingEmailChangeTests
{
    [Fact]
    public void ResolveConfirmBaseUrl_UsesConfiguredOrigin_WhenSet()
    {
        var result = PendingEmailChange.ResolveConfirmBaseUrl(
            "https://api.palne.shop", "https", "app.palne.shop");

        result.Should().Be("https://api.palne.shop");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveConfirmBaseUrl_FallsBackToRequestOrigin_WhenUnconfigured(string? configured)
    {
        var result = PendingEmailChange.ResolveConfirmBaseUrl(configured, "https", "api.palne.shop");

        result.Should().Be("https://api.palne.shop");
    }

    [Fact]
    public void ConfirmUrl_FromConfiguredOrigin_LandsOnTheApiHost_NotTheAdminHost()
    {
        // The admin dashboard request arrives on app.palne.shop, but the configured origin wins, so
        // the emailed link points at the API host that actually serves the confirm endpoint.
        var baseUrl = PendingEmailChange.ResolveConfirmBaseUrl(
            "https://api.palne.shop", "https", "app.palne.shop");

        var url = PendingEmailChange.BuildConfirmUrl(baseUrl, "TOKEN123");

        url.Should().Be("https://api.palne.shop/api/auth/email/confirm?token=TOKEN123");
    }
}

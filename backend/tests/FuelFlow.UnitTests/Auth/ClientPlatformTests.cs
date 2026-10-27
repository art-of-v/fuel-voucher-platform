using FluentAssertions;
using FuelFlow.SharedKernel.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace FuelFlow.UnitTests.Auth;

/// <summary>
/// Who counts as the native app, and therefore who gets the refresh token in a response body.
/// </summary>
/// <remarks>
/// The second half matters as much as the first: the App Store build shipped before the header
/// existed, so it identifies itself only by its User-Agent, and a fresh install that receives no
/// refresh token lands back on the phone screen (production, 2026-10-27). The browser cases are
/// pinned in the other direction — widening the body delivery to a browser would put the token
/// where any script on the origin can read it.
/// </remarks>
public sealed class ClientPlatformTests
{
    private static IHeaderDictionary Headers(string? userAgent = null, string? platform = null)
    {
        // Typed as the interface: UserAgent is an explicit implementation, so a concrete
        // HeaderDictionary does not expose it.
        IHeaderDictionary headers = new HeaderDictionary();
        if (userAgent is not null)
            headers.UserAgent = userAgent;
        if (platform is not null)
            headers[ClientPlatform.HeaderName] = platform;
        return headers;
    }

    [Fact]
    public void The_Header_Alone_Identifies_The_App()
    {
        ClientPlatform.IsNative(Headers(platform: "mobile")).Should().BeTrue();
    }

    [Fact]
    public void The_Header_Value_Is_Matched_Case_Insensitively()
    {
        ClientPlatform.IsNative(Headers(platform: "Mobile")).Should().BeTrue();
    }

    [Fact]
    public void A_Header_Saying_Something_Else_Is_Not_The_App()
    {
        ClientPlatform.IsNative(Headers(platform: "web")).Should().BeFalse();
    }

    [Theory]
    // What build 236 actually sends on iOS, taken from the production request log.
    [InlineData("FuelFlow/236 CFNetwork/3860.700.2 Darwin/25.6.0")]
    // React Native's fetch on Android, which sends no header either.
    [InlineData("okhttp/4.12.0")]
    public void The_Old_Bundle_Is_Recognized_By_Its_Own_User_Agent(string userAgent)
    {
        ClientPlatform.IsNative(Headers(userAgent: userAgent)).Should().BeTrue(
            "the shipped bundle predates the header and would otherwise never store a session");
    }

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/156.0.0.0 Safari/537.36")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Safari/605.1.15")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1")]
    [InlineData("")]
    public void A_Browser_Or_An_Unidentified_Caller_Gets_No_Token_In_The_Body(string userAgent)
    {
        ClientPlatform.IsNative(Headers(userAgent: userAgent)).Should().BeFalse(
            "a browser reads the token from its HttpOnly cookie, and an unknown caller gets no copy");
    }
}

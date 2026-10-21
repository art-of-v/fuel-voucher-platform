using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The acceptance criterion of the security finding S-9: the refresh token travels in the
/// response body only for the native mobile client, which has no cookie jar to keep it in. A
/// browser gets it in the HttpOnly cookie and nowhere else.
/// </summary>
[Collection("Integration Tests")]
public sealed class RefreshTokenTravelsToTheNativeClientOnly : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public RefreshTokenTravelsToTheNativeClientOnly(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task A_Browser_Caller_Gets_No_Refresh_Token_In_The_Body_And_Still_Has_The_Cookie()
    {
        using var client = _fixture.CreateClient();

        var verified = await VerifyAsync(client, "+380995500011", native: false);
        verified.Body.TryGetProperty("refreshToken", out var browserToken).Should().BeTrue(
            "the field is still there, so the client's shape does not change");
        browserToken.ValueKind.Should().Be(JsonValueKind.Null, "a browser must not get a copy in the body");

        // The cookie carries it instead. The test host speaks http, and a Secure cookie is not
        // sent over http, so the cookie is taken from the response and presented by hand - which
        // is exactly what a browser over https does.
        var cookie = verified.SetCookie;
        cookie.Should().NotBeNullOrEmpty("the browser gets the token in the cookie");
        cookie!.Should().Contain("refresh_token=");
        cookie.Should().Contain("httponly", "the cookie is HttpOnly, so no script can read it");

        // The cookie alone is enough: a refresh with no token in the body authenticates and
        // rotates, and still hands no token back.
        var refreshed = await RefreshAsync(client, body: null, native: false, cookie: cookie);
        refreshed.Body.TryGetProperty("refreshToken", out var rotatedToken).Should().BeTrue();
        rotatedToken.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task The_Native_Client_Gets_The_Refresh_Token_In_The_Body()
    {
        using var client = _fixture.CreateClient();

        var verified = await VerifyAsync(client, "+380995500012", native: true);
        verified.Body.TryGetProperty("refreshToken", out var token).Should().BeTrue();
        token.ValueKind.Should().Be(JsonValueKind.String, "the native app has no cookie jar to keep it in");
        token.GetString().Should().NotBeNullOrEmpty();

        // And it keeps getting it on every rotation, because the app stores it and sends it back.
        var refreshed = await RefreshAsync(
            client,
            body: new { refreshToken = token.GetString() },
            native: true,
            cookie: null);
        refreshed.Body.TryGetProperty("refreshToken", out var rotatedToken).Should().BeTrue();
        rotatedToken.ValueKind.Should().Be(JsonValueKind.String);
        rotatedToken.GetString().Should().NotBeNullOrEmpty();
        rotatedToken.GetString().Should().NotBe(token.GetString(), "a rotation issues a new token");
    }

    private async Task<(JsonElement Body, string? SetCookie)> VerifyAsync(
        HttpClient client,
        string phoneNumber,
        bool native)
    {
        var sendCode = await client.PostAsJsonAsync("/api/auth/send-code", new { phoneNumber });
        sendCode.EnsureSuccessStatusCode();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/verify")
        {
            Content = JsonContent.Create(new { phoneNumber, code = "000000" })
        };
        if (native)
            request.Headers.Add("X-Client-Platform", "mobile");

        var verify = await client.SendAsync(request);
        verify.EnsureSuccessStatusCode();

        var setCookie = verify.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? string.Join("; ", cookies)
            : null;

        return (await ReadJsonAsync(verify), setCookie);
    }

    private async Task<(JsonElement Body, string? SetCookie)> RefreshAsync(
        HttpClient client,
        object? body,
        bool native,
        string? cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        if (body is not null)
            request.Content = JsonContent.Create(body);
        if (native)
            request.Headers.Add("X-Client-Platform", "mobile");
        if (cookie is not null)
            request.Headers.Add("Cookie", cookie.Split(';')[0]);

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await ReadJsonAsync(response), null);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}

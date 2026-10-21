using System.Net.Http.Json;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Posts as the native mobile app.
/// </summary>
/// <remarks>
/// The refresh token travels in the response body only for the native client: a browser gets it
/// in the `HttpOnly` cookie and never in the body, because a script on the origin can read a
/// response body and cannot read an `HttpOnly` cookie (the backend's <c>ClientPlatform</c>). A
/// test that drives the mobile API and reads the token out of the body therefore has to be that
/// client — and saying so once, here, beats a header on every call site.
/// </remarks>
public static class NativeClient
{
    public static async Task<HttpResponseMessage> PostAsNativeAsync(
        this HttpClient client,
        string url,
        object? body = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = body is null ? null : JsonContent.Create(body)
        };
        request.Headers.Add("X-Client-Platform", "mobile");
        return await client.SendAsync(request);
    }
}

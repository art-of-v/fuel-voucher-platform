using Microsoft.AspNetCore.Http;

namespace FuelFlow.SharedKernel.Security;

/// <summary>
/// How a client says what it is, and what the server does with the answer.
/// </summary>
/// <remarks>
/// <para>
/// The refresh token is a long-lived bearer credential, and where it lives is a property of the
/// client, not of the endpoint. A browser has an <c>HttpOnly</c> cookie jar, and the token belongs
/// there: JavaScript never sees it, so an XSS cannot steal it. A native app has no cookie jar at
/// all — React Native keeps no cookies — so the token has to travel in the response body, into the
/// device's own secure storage.
/// </para>
/// <para>
/// Sending the token in both places to both kinds of client is what this class exists to stop:
/// the browser then holds a copy in the response body, where any script on the origin can read
/// it, and the rotation and reuse detection that bound the damage do not remove the copy.
/// </para>
/// </remarks>
public static class ClientPlatform
{
    /// <summary>
    /// The header a native client sends to say what it is. A browser never sends it.
    /// </summary>
    public const string HeaderName = "X-Client-Platform";

    /// <summary>
    /// The native mobile app.
    /// </summary>
    public const string NativeMobile = "mobile";

    /// <summary>
    /// Whether the caller is the native mobile app — the only client that gets the refresh token
    /// in the response body.
    /// </summary>
    public static bool IsNative(IHeaderDictionary headers)
        => headers.TryGetValue(HeaderName, out var value)
           && string.Equals(value.ToString(), NativeMobile, StringComparison.OrdinalIgnoreCase);
}

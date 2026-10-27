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
/// <para>
/// The header arrived after the App Store build that needed it, so the app is also recognized by
/// what it cannot help telling us: its own User-Agent. Build 236 (submitted 2026-10-14) sends
/// <c>FuelFlow/&lt;build&gt; CFNetwork/… Darwin/…</c> on iOS and OkHttp's default <c>okhttp/…</c>
/// on Android, and no header at all — the header did not exist when that bundle was built. Without
/// this the first session of a fresh install breaks: the app receives no refresh token, stores no
/// session, and the first <c>/api/auth/user/me</c> answers 401, which is the "signed in, then
/// thrown back to the phone screen" report from production on 2026-10-27.
/// </para>
/// <para>
/// The fallback does not widen what a browser can do. <c>User-Agent</c> is a forbidden header:
/// script cannot set it, so a page on the origin cannot present itself as the app, and a browser
/// sends neither prefix. The header was never a security control in the first place — the docs call
/// it a rollout hint a caller may set freely — so recognizing the app a second way costs nothing.
/// The control is that a browser reads the token from its cookie jar and not from a response body.
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
    /// The app's own User-Agent as React Native writes it on iOS: <c>FuelFlow/&lt;build&gt;</c>
    /// followed by the CFNetwork and Darwin tokens.
    /// </summary>
    private const string IosUserAgentPrefix = "FuelFlow/";

    /// <summary>
    /// React Native's <c>fetch</c> on Android is OkHttp, which sends its own default User-Agent
    /// and nothing of ours.
    /// </summary>
    private const string AndroidUserAgentPrefix = "okhttp/";

    /// <summary>
    /// Whether the caller is the native mobile app — the only client that gets the refresh token
    /// in the response body.
    /// </summary>
    public static bool IsNative(IHeaderDictionary headers)
        => DeclaresItself(headers) || LooksLikeTheApp(headers);

    private static bool DeclaresItself(IHeaderDictionary headers)
        => headers.TryGetValue(HeaderName, out var value)
           && string.Equals(value.ToString(), NativeMobile, StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeTheApp(IHeaderDictionary headers)
    {
        var userAgent = headers.UserAgent.ToString();
        return userAgent.StartsWith(IosUserAgentPrefix, StringComparison.OrdinalIgnoreCase)
            || userAgent.StartsWith(AndroidUserAgentPrefix, StringComparison.OrdinalIgnoreCase);
    }
}

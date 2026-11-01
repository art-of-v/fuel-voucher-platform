using System.Text;
using Microsoft.AspNetCore.Http;

namespace FuelFlow.SharedKernel.Security;

/// <summary>
/// Decides whether a request path is one of a configured set of protected endpoints.
/// </summary>
/// <remarks>
/// <para>
/// This is a security boundary, so the comparison has to be at least as permissive as ASP.NET Core
/// routing. Routing matches <c>/api/purchases/</c> against <c>api/purchases</c>; a raw string
/// comparison does not, and a single appended slash would reach the checkout handler with no
/// proof at all. Repeated and trailing separators are normalised away first.
/// </para>
/// <para>
/// Two pattern shapes are supported: an exact path, and a path whose segments may contain a
/// <c>*</c> wildcard (for example <c>/api/vouchers/*/mark-used</c>). A trailing <c>/*</c> is
/// treated as a prefix, matching the endpoint and everything beneath it, which is how the device
/// signature list already reads. Over-matching is deliberately safe: a path variant routing would
/// not have dispatched answers 401 instead of 404. Under-matching silently disables the check
/// (attestation, or the device proof) on a money endpoint, which is the failure this class
/// exists to prevent.
/// </para>
/// </remarks>
public static class RequestPathMatcher
{
    public static bool IsProtected(PathString path, IReadOnlyCollection<string> patterns)
    {
        var normalizedPath = Normalize(path.Value);

        foreach (var pattern in patterns)
        {
            if (Matches(normalizedPath, Normalize(pattern)))
                return true;
        }

        return false;
    }

    private static bool Matches(string path, string pattern)
    {
        if (pattern.Length == 0)
            return false;

        // "/api/devices/*" covers "/api/devices" and everything beneath it.
        if (pattern.EndsWith("/*", StringComparison.Ordinal)
            && pattern.Count(c => c == '*') == 1)
        {
            var prefix = pattern[..^2];

            return path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
        }

        var pathSegments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var patternSegments = pattern.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (pathSegments.Length != patternSegments.Length)
            return false;

        for (var i = 0; i < patternSegments.Length; i++)
        {
            if (patternSegments[i] == "*")
                continue;

            if (!pathSegments[i].Equals(patternSegments[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Collapses repeated '/' and strips any trailing '/', so "/api/purchases",
    /// "/api/purchases/" and "//api//purchases//" all compare equal. A bare "/" is preserved.
    /// </summary>
    internal static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        var previousWasSlash = false;

        foreach (var c in value)
        {
            if (c == '/')
            {
                if (!previousWasSlash)
                    builder.Append(c);

                previousWasSlash = true;
            }
            else
            {
                builder.Append(c);
                previousWasSlash = false;
            }
        }

        if (builder.Length > 1 && builder[^1] == '/')
            builder.Length--;

        return builder.ToString();
    }
}

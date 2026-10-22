using Microsoft.AspNetCore.Http.Metadata;

namespace FuelFlow.Middleware;

/// <summary>
/// The request-body ceiling, in one place, with the rule that resolves it for one request.
/// </summary>
/// <remarks>
/// <para>
/// The ceiling used to be 25 MB <i>globally</i>, because 25 MB is what a voucher-PDF import
/// needs. A global ceiling is a ceiling on every route, so every endpoint - including the
/// anonymous ones (<c>POST /api/support/messages</c>, <c>POST /api/auth/send-code</c>,
/// <c>POST /api/monobank/webhook</c>) - could be handed 25 MB of body, and the global rate
/// limit of 300 requests a minute per source address turns that into about 7.5 GB a minute of
/// buffering against a container with an 896 MB memory limit.
/// </para>
/// <para>
/// So the ceiling is now what a JSON body needs - <see cref="DefaultMaxRequestBodySize"/>, one
/// mebibyte - and the routes that genuinely accept a file raise it for themselves with
/// <c>[RequestSizeLimit]</c>. That attribute is the single source of truth: the middleware
/// reads it off the endpoint's metadata, Kestrel reads it off the same request, and the guard
/// test reads it off the same metadata, so the number cannot drift between the three.
/// </para>
/// </remarks>
internal static class RequestSizeLimits
{
    /// <summary>
    /// The global ceiling: one mebibyte, which is generous for a JSON body and about the size
    /// of a screenshot. It is the limit for every route that does not raise it for itself.
    /// </summary>
    internal const long DefaultMaxRequestBodySize = 1_048_576;

    /// <summary>
    /// The ceiling for the routes that accept a file: the voucher-PDF import and the other
    /// uploads. It is the largest body the API accepts on any route, and the guard test fails
    /// if a route declares more than this.
    /// </summary>
    internal const long FileUploadMaxRequestBodySize = 25_000_000;

    /// <summary>
    /// The ceiling that applies to this request: the one its endpoint declares, or the global
    /// one when the endpoint declares none.
    /// </summary>
    /// <param name="context">The request. Its endpoint is already resolved - the routing
    /// middleware runs before this one - so the declaration is on the endpoint's metadata.</param>
    internal static long For(HttpContext context)
    {
        var declared = context.GetEndpoint()?.Metadata.GetMetadata<IRequestSizeLimitMetadata>();

        return declared?.MaxRequestBodySize ?? DefaultMaxRequestBodySize;
    }
}

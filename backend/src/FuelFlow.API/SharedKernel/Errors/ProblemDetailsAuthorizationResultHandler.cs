using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace FuelFlow.SharedKernel.Errors;

/// <summary>
/// Makes the authorization failures leave as the one error envelope.
/// </summary>
/// <remarks>
/// <para>
/// A 401 and a 403 are the most common failures this API has - an expired token, a staff role
/// that is not allowed to do the thing - and by default the framework answers both with an
/// EMPTY body. That is a second error shape: a client that wants to say why it was refused has
/// nothing to read, and one that guesses reads whatever it can find in the body.
/// </para>
/// <para>
/// The headers are kept exactly as the framework sets them: <c>WWW-Authenticate: Bearer</c> for
/// a challenge, and the <c>insufficient_scope</c> error for a refusal. Only the body changes -
/// from nothing to the document every other failure uses.
/// </para>
/// </remarks>
internal sealed class ProblemDetailsAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Challenged)
        {
            context.Response.Headers.WWWAuthenticate = "Bearer";
            await ApiErrorResponse.WriteAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "Unauthorized");
            return;
        }

        if (authorizeResult.Forbidden)
        {
            context.Response.Headers.WWWAuthenticate = "Bearer error=\"insufficient_scope\"";
            await ApiErrorResponse.WriteAsync(
                context,
                StatusCodes.Status403Forbidden,
                "Forbidden");
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}

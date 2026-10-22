using Microsoft.AspNetCore.Http;

namespace FuelFlow.SharedKernel.Errors;

/// <summary>
/// Writes the one error envelope from a place that has no controller to return a result from -
/// a middleware that refuses a request before it reaches an endpoint.
/// </summary>
public static class ApiErrorResponse
{
    /// <summary>
    /// Writes the envelope with the request path and the correlation id on it, and stops the
    /// pipeline: the response is complete.
    /// </summary>
    public static Task WriteAsync(
        HttpContext context,
        int status,
        string detail,
        string? code = null,
        IReadOnlyDictionary<string, object?>? data = null,
        CancellationToken cancellationToken = default)
    {
        var problem = ApiError.Problem(status, detail, code, data);
        problem.Instance = context.Request.Path.Value;
        problem.Extensions[ApiError.TraceIdExtension] = context.TraceIdentifier;

        context.Response.StatusCode = status;

        return context.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: ApiError.ProblemJsonContentType,
            cancellationToken: cancellationToken);
    }
}

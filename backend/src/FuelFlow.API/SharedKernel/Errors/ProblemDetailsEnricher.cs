using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FuelFlow.SharedKernel.Errors;

/// <summary>
/// Stamps the envelope in the one place every failure passes through, so that no failure can
/// arrive without the same fields.
/// </summary>
/// <remarks>
/// A controller that returns <see cref="ApiError"/> gets the request path and the correlation id
/// from here. Without this filter every failure would have to remember them itself, and the
/// first one that forgot would be the one the operator cannot correlate with the logs.
/// </remarks>
public sealed class ProblemDetailsEnricher : IResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is not ObjectResult { Value: ProblemDetails problem } objectResult)
        {
            return;
        }

        problem.Instance ??= context.HttpContext.Request.Path.Value;
        problem.Extensions[ApiError.TraceIdExtension] = context.HttpContext.TraceIdentifier;

        // The media type is part of the contract: a client that reads the body as a problem
        // document has to be able to ask for it by name.
        objectResult.ContentTypes.Clear();
        objectResult.ContentTypes.Add(ApiError.ProblemJsonContentType);
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
        // Nothing to do: the envelope is written by the formatter.
    }
}

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.SharedKernel.Errors;

/// <summary>
/// The one shape this API fails in.
/// </summary>
/// <remarks>
/// <para>
/// Every failure the API returns - a controller's rejection, the global exception handler's
/// answer, a middleware that refuses a request - is an RFC 9457 <see cref="ProblemDetails"/>
/// document with the same media type (<c>application/problem+json</c>) and the same fields:
/// </para>
/// <list type="bullet">
/// <item><description><c>title</c> - the status, in words ("Bad Request", "Not Found").</description></item>
/// <item><description><c>detail</c> - what went wrong, for the human who reads it.</description></item>
/// <item><description><c>status</c> - the HTTP status code.</description></item>
/// <item><description><c>type</c> - the RFC 9110 section that defines the status.</description></item>
/// <item><description><c>instance</c> - the request path, stamped by the enricher.</description></item>
/// <item><description><c>traceId</c> - the correlation id, stamped by the enricher.</description></item>
/// <item><description><c>code</c> - a stable machine code, when the failure has one.</description></item>
/// <item><description><c>data</c> - the numbers a coded failure carries, when it has them.</description></item>
/// </list>
/// <para>
/// It used to be three shapes: <c>{ error = ... }</c> from most controllers, <c>{ message = ... }</c>
/// from the auth ones and <c>ProblemDetails</c> from the exception handler. A client could not
/// tell them apart, so the admin client sniffed four body shapes to find the message and the
/// contract was, in effect, <c>any</c>. One factory is what makes the contract a contract; the
/// guard that keeps it one lives in
/// <c>backend/tests/FuelFlow.UnitTests/Architecture/DependencyRules/OneErrorEnvelopeTests.cs</c>.
/// </para>
/// <para>
/// The <c>code</c> and <c>data</c> extensions survive as ProblemDetails extensions, so they are
/// serialised flat into the same document - a client reads one body and finds the machine code
/// beside the sentence. A failure that needs to say more than a sentence and a code puts it in
/// <c>extra</c>, which lands in the same document under its own name.
/// </para>
/// </remarks>
public static class ApiError
{
    /// <summary>The extension key the machine code travels under.</summary>
    public const string CodeExtension = "code";

    /// <summary>The extension key the numbers of a coded failure travel under.</summary>
    public const string DataExtension = "data";

    /// <summary>The extension key the correlation id travels under.</summary>
    public const string TraceIdExtension = "traceId";

    /// <summary>The media type an RFC 9457 document is served with.</summary>
    public const string ProblemJsonContentType = "application/problem+json";

    /// <summary>
    /// The sentence every 500 carries. It says nothing about the fault on purpose: the fault is
    /// in the logs, and the body must not tell an anonymous caller what broke inside.
    /// </summary>
    public const string UnexpectedError = "An unexpected error occurred";

    public static ObjectResult BadRequest(
        string? detail,
        string? code = null,
        IReadOnlyDictionary<string, object?>? data = null,
        IReadOnlyDictionary<string, object?>? extra = null)
        => WithProblemType(new BadRequestObjectResult(
            Problem(StatusCodes.Status400BadRequest, detail, code, data, extra)));

    public static ObjectResult Unauthorized(
        string? detail,
        string? code = null,
        IReadOnlyDictionary<string, object?>? data = null,
        IReadOnlyDictionary<string, object?>? extra = null)
        => WithProblemType(new UnauthorizedObjectResult(
            Problem(StatusCodes.Status401Unauthorized, detail, code, data, extra)));

    public static ObjectResult Forbidden(
        string? detail,
        string? code = null,
        IReadOnlyDictionary<string, object?>? data = null,
        IReadOnlyDictionary<string, object?>? extra = null)
        => WithProblemType(new ObjectResult(
            Problem(StatusCodes.Status403Forbidden, detail, code, data, extra))
        {
            StatusCode = StatusCodes.Status403Forbidden,
        });

    public static ObjectResult NotFound(
        string? detail,
        string? code = null,
        IReadOnlyDictionary<string, object?>? data = null,
        IReadOnlyDictionary<string, object?>? extra = null)
        => WithProblemType(new NotFoundObjectResult(
            Problem(StatusCodes.Status404NotFound, detail, code, data, extra)));

    public static ObjectResult Conflict(
        string? detail,
        string? code = null,
        IReadOnlyDictionary<string, object?>? data = null,
        IReadOnlyDictionary<string, object?>? extra = null)
        => WithProblemType(new ConflictObjectResult(
            Problem(StatusCodes.Status409Conflict, detail, code, data, extra)));

    public static ObjectResult BadGateway(
        string? detail,
        string? code = null,
        IReadOnlyDictionary<string, object?>? data = null,
        IReadOnlyDictionary<string, object?>? extra = null)
        => WithProblemType(new ObjectResult(
            Problem(StatusCodes.Status502BadGateway, detail, code, data, extra))
        {
            StatusCode = StatusCodes.Status502BadGateway,
        });

    public static ObjectResult ServiceUnavailable(
        string? detail,
        string? code = null,
        IReadOnlyDictionary<string, object?>? data = null,
        IReadOnlyDictionary<string, object?>? extra = null)
        => WithProblemType(new ObjectResult(
            Problem(StatusCodes.Status503ServiceUnavailable, detail, code, data, extra))
        {
            StatusCode = StatusCodes.Status503ServiceUnavailable,
        });

    /// <summary>
    /// A validation failure: the same document, with the fields that broke beside it.
    /// </summary>
    /// <remarks>
    /// RFC 9457 §3.1 gives a validation problem its own member, <c>errors</c>, and this is where
    /// it lives. It is still one envelope - a <see cref="ValidationProblemDetails"/> is a
    /// <see cref="ProblemDetails"/> - so a client reads one body and finds the sentence, the
    /// status and the offending fields in it.
    /// </remarks>
    public static ObjectResult Validation(
        IReadOnlyDictionary<string, string[]> errors,
        string? detail = null)
    {
        var problem = new ValidationProblemDetails(
            errors.ToDictionary(pair => pair.Key, pair => pair.Value))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = Title(StatusCodes.Status400BadRequest),
            Detail = detail ?? "One or more validation errors occurred.",
            Type = Type(StatusCodes.Status400BadRequest),
        };

        return WithProblemType(new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status400BadRequest,
        });
    }

    /// <summary>
    /// The envelope as an MVC result. Controllers return this; the enricher stamps the
    /// <c>instance</c> and the <c>traceId</c> onto it before it is written.
    /// </summary>
    public static ObjectResult Result(
        int status,
        string? detail,
        string? code = null,
        IReadOnlyDictionary<string, object?>? data = null,
        IReadOnlyDictionary<string, object?>? extra = null)
        => WithProblemType(new ObjectResult(Problem(status, detail, code, data, extra))
        {
            StatusCode = status,
        });

    /// <summary>
    /// The media type of the answer. The result carries the framework's own type for the status
    /// - a 404 is still a <c>NotFoundObjectResult</c> - and the body is the problem document.
    /// </summary>
    private static T WithProblemType<T>(T result) where T : ObjectResult
    {
        result.ContentTypes.Clear();
        result.ContentTypes.Add(ProblemJsonContentType);
        return result;
    }

    /// <summary>
    /// The envelope as a document. The middleware writes this directly - it has no controller to
    /// return an <see cref="ObjectResult"/> from.
    /// </summary>
    public static ProblemDetails Problem(
        int status,
        string? detail,
        string? code = null,
        IReadOnlyDictionary<string, object?>? data = null,
        IReadOnlyDictionary<string, object?>? extra = null)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = Title(status),
            Detail = detail,
            Type = Type(status),
        };

        if (!string.IsNullOrWhiteSpace(code))
        {
            problem.Extensions[CodeExtension] = code;
        }

        if (data is { Count: > 0 })
        {
            problem.Extensions[DataExtension] = data;
        }

        if (extra is { Count: > 0 })
        {
            foreach (var pair in extra)
            {
                problem.Extensions[pair.Key] = pair.Value;
            }
        }

        return problem;
    }

    /// <summary>
    /// The status in words. It is the same for every failure of that status, so a client can
    /// render it without reading anything else.
    /// </summary>
    public static string Title(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "Bad Request",
        StatusCodes.Status401Unauthorized => "Unauthorized",
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status422UnprocessableEntity => "Unprocessable Entity",
        StatusCodes.Status429TooManyRequests => "Too Many Requests",
        StatusCodes.Status500InternalServerError => "Internal Server Error",
        StatusCodes.Status502BadGateway => "Bad Gateway",
        StatusCodes.Status503ServiceUnavailable => "Service Unavailable",
        _ => "Error",
    };

    /// <summary>The RFC 9110 section that defines the status.</summary>
    public static string Type(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        StatusCodes.Status401Unauthorized => "https://tools.ietf.org/html/rfc9110#section-15.5.2",
        StatusCodes.Status403Forbidden => "https://tools.ietf.org/html/rfc9110#section-15.5.4",
        StatusCodes.Status404NotFound => "https://tools.ietf.org/html/rfc9110#section-15.5.5",
        StatusCodes.Status409Conflict => "https://tools.ietf.org/html/rfc9110#section-15.5.10",
        StatusCodes.Status422UnprocessableEntity => "https://tools.ietf.org/html/rfc9110#section-15.5.21",
        StatusCodes.Status429TooManyRequests => "https://tools.ietf.org/html/rfc9110#section-15.5.29",
        StatusCodes.Status500InternalServerError => "https://tools.ietf.org/html/rfc9110#section-15.6.1",
        StatusCodes.Status502BadGateway => "https://tools.ietf.org/html/rfc9110#section-15.6.3",
        StatusCodes.Status503ServiceUnavailable => "https://tools.ietf.org/html/rfc9110#section-15.6.4",
        _ => "about:blank",
    };
}

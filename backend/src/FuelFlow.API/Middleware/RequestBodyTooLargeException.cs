namespace FuelFlow.Middleware;

/// <summary>
/// Thrown when a body turns out to be larger than the ceiling allows <i>while it is being
/// read</i> - the case a declared <c>Content-Length</c> cannot catch.
/// </summary>
internal sealed class RequestBodyTooLargeException : Exception
{
    public RequestBodyTooLargeException(long limit)
        : base($"The request body exceeds the {limit} byte limit.")
    {
        Limit = limit;
    }

    /// <summary>The ceiling that was exceeded.</summary>
    public long Limit { get; }
}

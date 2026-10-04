namespace FuelFlow.SharedKernel.Domain;

public sealed class ErrorLog
{
    public Guid Id { get; set; }
    public DateTime LoggedAtUtc { get; set; }
    public string Level { get; set; } = null!;
    public string Message { get; set; } = null!;
    public string? ExceptionType { get; set; }
    public string? ExceptionMessage { get; set; }
    public string? StackTrace { get; set; }
    public string? Source { get; set; }
    public string? RequestPath { get; set; }
    public string? RequestMethod { get; set; }
    public string? UserName { get; set; }

    /// <summary>
    /// W3C trace id of the activity that was current when the record was logged. One failed
    /// request emits several records (request logging, the global handler, the feature handler,
    /// EF Core), and this is what lets the admin log group them back into a single incident.
    /// Null for records logged outside any activity - startup, shutdown, background jobs.
    /// </summary>
    public string? TraceId { get; set; }

    /// <summary>
    /// When staff acknowledged this error as handled, or null while it is still outstanding.
    /// A fault is only "resolved" once a human says so - the log cannot tell whether a
    /// deployment fixed the cause - so the journal hides acknowledged rows by default.
    /// </summary>
    public DateTime? ResolvedAtUtc { get; set; }

    public Guid? ResolvedByUserId { get; set; }

    public string? ResolvedByUserName { get; set; }
}

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
}

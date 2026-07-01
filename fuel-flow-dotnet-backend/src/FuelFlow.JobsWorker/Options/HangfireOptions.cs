namespace FuelFlow.JobsWorker.Options;

public sealed class HangfireOptions
{
    public const string SectionName = "Hangfire";

    public string DashboardPath { get; set; } = "/hangfire";
    public string Username { get; set; } = "admin";
    public string Password { get; set; } = "changeme123";
}

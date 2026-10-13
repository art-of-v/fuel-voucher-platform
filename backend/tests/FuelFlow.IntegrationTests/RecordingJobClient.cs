using Hangfire;
using Hangfire.Common;
using Hangfire.States;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// A Hangfire client that records what was queued instead of queueing it, so a test can assert the
/// triage job was scheduled and then run it itself in a fresh scope.
/// <para>
/// It implements only the two members <see cref="IBackgroundJobClient"/> actually declares
/// (<c>Create</c> and <c>ChangeState</c>). Every <c>Enqueue</c>/<c>Schedule</c>/<c>RecurringJob</c>
/// is an <em>extension</em> method that funnels into <c>Create(Job, IState)</c> - so recording
/// there is both the only place it works and the only place that sees the serialised arguments.
/// </para>
/// </summary>
public sealed class RecordingJobClient : IBackgroundJobClient
{
    public List<Job> Created { get; } = [];

    /// <summary>The ids passed to <c>FeedbackTriageService.TriageAsync</c>, in order.</summary>
    public List<Guid> TriagedMessageIds => Created
        .Where(job => job.Type == typeof(API.BackgroundJobs.FeedbackTriageService))
        .Select(job => job.Args.OfType<Guid>().FirstOrDefault())
        .ToList();

    public string Create(Job job, IState state)
    {
        Created.Add(job);
        return $"recording-{Created.Count}";
    }

    public void ChangeState(string backgroundJobId, IState state, bool suppressNotifications = false)
    {
    }

    public bool ChangeState(string backgroundJobId, IState state, string reason) => false;
}
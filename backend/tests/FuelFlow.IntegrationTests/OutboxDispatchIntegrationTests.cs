using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.Features.Notifications.Push;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.Testing;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The outbox dispatcher, and the safety net it replaced.
/// </summary>
/// <remarks>
/// <para>
/// The outbox exists so that a domain event is written in the same transaction as the state change
/// that caused it. What used to pick those rows up was three recurring jobs that polled for them
/// once a minute each - 4,320 wake-ups a day to look at an empty table, and a customer waiting up
/// to two minutes for the notification of a purchase they had already paid for.
/// </para>
/// <para>
/// Now the row is dispatched by a job the outbox interceptor queues when the row is written, and
/// the recurring jobs are the safety net for the case where the queueing did not happen. Two
/// properties matter and are asserted here: the fast path fires, and the safety net does not
/// race it.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class OutboxDispatchIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public OutboxDispatchIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext(params SaveChangesInterceptor[] interceptors)
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .AddInterceptors(interceptors)
            .Options, TestQrCipher.Instance);

    [Fact]
    public async Task Writing_An_Outbox_Event_Queues_A_Dispatch_Job()
    {
        var jobClient = new Mock<IBackgroundJobClient>();
        IState? queued = null;
        jobClient
            .Setup(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>()))
            .Callback<Job, IState>((_, state) => queued = state)
            .Returns("dispatch-job");

        var interceptor = new OutboxDispatchInterceptor(jobClient.Object, NullLogger<OutboxDispatchInterceptor>.Instance);

        await using (var context = CreateContext(interceptor))
        {
            context.OutboxEvents.Add(new OutboxEvent
            {
                EventType = OutboxEventType.OrderFulfilled,
                Payload = "{}",
                CreatedAtUtc = TestClock.UtcNow
            });
            await context.SaveChangesAsync();
        }

        jobClient.Verify(
            c => c.Create(It.IsAny<Job>(), It.IsAny<IState>()),
            Times.Once());

        // `Schedule` is an extension method over `Create(Job, IState)`, so the state is where the
        // delay shows up. A job queued for NOW would be picked up before the transaction that
        // wrote the row had committed, on any caller that manages its own transaction.
        queued.Should().BeOfType<ScheduledState>(
            "the dispatch is scheduled a few seconds out, not queued for now");
        ((ScheduledState)queued!).EnqueueAt.Should().BeAfter(TestClock.UtcNow.AddSeconds(3));
    }

    [Fact]
    public async Task The_Safety_Net_Leaves_An_Event_The_Fast_Path_Is_About_To_Claim()
    {
        // Through the SERVICE, not through a copy of its predicate: a test that re-implemented the
        // query would keep passing after the service stopped using it, which is the half of this
        // that actually matters.
        //
        // The claim window is the whole reason the two cannot both act on one event. For
        // OrderFulfilled that would mean the customer gets the notification twice.
        await using var context = CreateContext();

        var marker = System.Text.Json.JsonSerializer.Serialize(new
        {
            marker = $"outbox-dispatch-test-{Guid.CreateVersion7()}"
        });

        var justWritten = new OutboxEvent
        {
            EventType = OutboxEventType.OrderFulfilled,
            Payload = marker,
            CreatedAtUtc = TestClock.UtcNow
        };
        var old = new OutboxEvent
        {
            EventType = OutboxEventType.OrderFulfilled,
            Payload = marker,
            CreatedAtUtc = TestClock.UtcNow - OutboxDispatchJob.SafetyNetClaimWindow - TimeSpan.FromMinutes(5)
        };

        context.OutboxEvents.Add(justWritten);
        context.OutboxEvents.Add(old);
        await context.SaveChangesAsync();

        var pushSender = new Mock<IExpoPushSender>();
        var service = new NotificationService(
            context,
            pushSender.Object,
            NullLogger<NotificationService>.Instance);

        await service.ProcessOrderFulfilledEventsAsync();

        var states = await context.OutboxEvents
            .Where(e => e.Payload == marker)
            .ToDictionaryAsync(e => e.Id, e => e.Processed);

        states[old.Id].Should().BeTrue(
            "an event old enough to have been missed is the safety net's to claim");
        states[justWritten.Id].Should().BeFalse(
            "an event the fast path is about to claim is left alone - claiming it too would send the "
            + "customer the notification twice");

        await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM outbox_events WHERE payload = {0}::jsonb", marker);
    }

    [Fact]
    public void The_Claim_Window_Is_Longer_Than_The_Dispatch_Delay()
    {
        // The fast path schedules the job a few seconds out; the safety net must not start looking
        // before that has had time to run, or the two overlap.
        OutboxDispatchJob.SafetyNetClaimWindow.Should().BeGreaterThan(TimeSpan.FromSeconds(30));
    }
}
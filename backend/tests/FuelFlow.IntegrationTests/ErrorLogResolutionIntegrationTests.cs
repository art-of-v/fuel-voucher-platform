using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.Features.ErrorLogs;
using FuelFlow.Features.Providers;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Real-Postgres coverage for acknowledging errors. The journal is only useful if it opens on
/// what still needs attention, so the read path defaults to unresolved and every state change is
/// recorded with who did it - both covered here against the real table and the real audit sink.
/// </summary>
[Collection("Integration Tests")]
public sealed class ErrorLogResolutionIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public ErrorLogResolutionIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetAll_ByDefault_ShowsOnlyUnresolvedErrors()
    {
        await SeedAsync(logs =>
        {
            logs.Add(NewRow("outstanding one"));
            logs.Add(NewRow("outstanding two"));
            logs.Add(NewRow("already handled", resolvedBy: StaffId, resolvedName: "Test Staff"));
        });

        await using var context = CreateContext();
        var controller = NewController(context);

        (await ReadAsync(controller.GetAll())).Should().HaveCount(2);
        (await ReadAsync(controller.GetAll(resolved: ErrorLogsController.ResolutionFilter.All))).Should().HaveCount(3);

        var handled = await ReadAsync(controller.GetAll(resolved: ErrorLogsController.ResolutionFilter.Resolved));
        handled.Should().ContainSingle();
        handled[0].Message.Should().Be("already handled");
        handled[0].ResolvedByUserId.Should().Be(StaffId);
        handled[0].ResolvedByUserName.Should().Be("Test Staff");
        handled[0].ResolvedAtUtc.Should().NotBeNull();

        // A junk value must not silently reveal everything; anything unrecognised means
        // "outstanding", the same default a caller that knows nothing gets.
        (await ReadAsync(controller.GetAll(resolved: (ErrorLogsController.ResolutionFilter)99))).Should().HaveCount(2);
    }

    [Fact]
    public async Task Resolve_StampsWhoAndWhen_AndIsIdempotent()
    {
        var id = await SeedOneAsync(logs => logs.Add(NewRow("unhandled")));
        await using var context = CreateContext();
        var controller = NewController(context);

        var first = await ResolveAsync(controller, new ResolveErrorLogsRequest { Ids = [id] });
        first.Changed.Should().Be(1);

        var stamped = (await ReadRawAsync([id])).Single();
        stamped.ResolvedAtUtc.Should().NotBeNull();
        stamped.ResolvedByUserId.Should().Be(StaffId);
        stamped.ResolvedByUserName.Should().Be("Test Staff");

        // Re-posting the same id must not rewrite who handled it - a double-click is not a
        // reassignment, and the journal is the record of that decision.
        var second = await ResolveAsync(controller, new ResolveErrorLogsRequest { Ids = [id] });
        second.Changed.Should().Be(0);

        var afterDoubleClick = (await ReadRawAsync([id])).Single();
        afterDoubleClick.ResolvedAtUtc.Should().Be(stamped.ResolvedAtUtc);
        afterDoubleClick.ResolvedByUserId.Should().Be(StaffId);

        await using var verify = CreateContext();
        (await verify.Set<ProviderEventOutbox>().CountAsync(o => o.EventType == "ErrorLogsResolved"))
            .Should().Be(1, "the audit log records the decision, not the click");
    }

    [Fact]
    public async Task Resolve_ClosesAWholeIncidentInOneCall()
    {
        var traceId = Guid.NewGuid().ToString();
        var ids = await SeedAsync(logs =>
        {
            foreach (var message in new[] { "handler", "feature handler", "EF Core" })
                logs.Add(NewRow(message, traceId: traceId));
            logs.Add(NewRow("an unrelated failure", traceId: Guid.NewGuid().ToString()));
        });

        await using var context = CreateContext();
        var controller = NewController(context);

        // A fault fills the journal with one record per layer; closing them one at a time is
        // the chore this replaces.
        var result = await ResolveAsync(controller, new ResolveErrorLogsRequest { Ids = ids });
        result.Changed.Should().Be(4);

        var rows = await ReadRawAsync(ids);
        rows.Should().OnlyContain(r => r.ResolvedAtUtc != null);
        (await ReadAsync(controller.GetAll())).Should().BeEmpty();
    }

    [Fact]
    public async Task Reopen_ClearsTheStamp_AndIsRecorded()
    {
        var id = await SeedOneAsync(logs => logs.Add(NewRow("unhandled")));
        await using var context = CreateContext();
        var controller = NewController(context);

        await ResolveAsync(controller, new ResolveErrorLogsRequest { Ids = [id] });

        var reopened = await ResolveAsync(controller, new ResolveErrorLogsRequest { Ids = [id], Resolved = false });
        reopened.Changed.Should().Be(1);

        var row = (await ReadRawAsync([id])).Single();
        row.ResolvedAtUtc.Should().BeNull();
        row.ResolvedByUserId.Should().BeNull();
        row.ResolvedByUserName.Should().BeNull();

        // Back in the default view, where a still-unhandled fault belongs.
        (await ReadAsync(controller.GetAll())).Should().ContainSingle();

        await using var verify = CreateContext();
        (await verify.Set<ProviderEventOutbox>().CountAsync(o => o.EventType == "ErrorLogsReopened")).Should().Be(1);
    }

    [Fact]
    public async Task Resolve_WithoutIds_IsRejected()
    {
        await using var context = CreateContext();
        var controller = NewController(context);

        var result = await controller.Resolve(new ResolveErrorLogsRequest(), default);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    private static readonly Guid StaffId = Guid.NewGuid();

    private ErrorLogsController NewController(ApplicationDbContext context)
        => new(context, new ProviderEventService(context))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, StaffId.ToString()),
                        new Claim(ClaimTypes.Name, "+380110010203"),
                        new Claim("first_name", "Test"),
                        new Claim("last_name", "Staff")
                    ], "TestAuth"))
                }
            }
        };

    private static ErrorLog NewRow(string message, string? traceId = null, Guid? resolvedBy = null, string? resolvedName = null) => new()
    {
        Id = Guid.NewGuid(),
        LoggedAtUtc = DateTime.UtcNow,
        Level = "Error",
        Message = message,
        Source = "FuelFlow.Middleware.GlobalExceptionHandler",
        TraceId = traceId,
        ResolvedAtUtc = resolvedBy is null ? null : DateTime.UtcNow.AddMinutes(-5),
        ResolvedByUserId = resolvedBy,
        ResolvedByUserName = resolvedName
    };

    /// <summary>Returns the ids it seeded, so the caller can act on exactly those rows.</summary>
    private async Task<List<Guid>> SeedAsync(Action<ApplicationDbContext> seed)
    {
        using var context = CreateContext();
        await context.Database.MigrateAsync();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"error_logs\", \"provider_event_outbox\" RESTART IDENTITY CASCADE");
        seed(context);
        await context.SaveChangesAsync();
        return context.ChangeTracker.Entries<ErrorLog>().Select(e => e.Entity.Id).ToList();
    }

    private async Task<Guid> SeedOneAsync(Action<ApplicationDbContext> seed)
    {
        var ids = await SeedAsync(seed);
        ids.Should().ContainSingle();
        return ids[0];
    }

    private async Task<List<ErrorLog>> ReadRawAsync(IReadOnlyCollection<Guid> ids)
    {
        await using var context = CreateContext();
        return await context.ErrorLogs.AsNoTracking().Where(e => ids.Contains(e.Id)).ToListAsync();
    }

    private static async Task<List<ErrorLogDto>> ReadAsync(Task<IActionResult> result)
    {
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var page = JsonSerializer.Deserialize<Page<ErrorLogDto>>(
            JsonSerializer.Serialize(ok.Value),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        page.Should().NotBeNull();
        return page!.Items;
    }

    private static async Task<ResolveOutcome> ResolveAsync(ErrorLogsController controller, ResolveErrorLogsRequest request)
    {
        var result = await controller.Resolve(request, default);
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var outcome = JsonSerializer.Deserialize<ResolveOutcome>(
            JsonSerializer.Serialize(ok.Value),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        outcome.Should().NotBeNull();
        return outcome!;
    }

    private sealed record Page<T>(int Total, List<T> Items);

    private sealed record ResolveOutcome(bool Success, int Changed);

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.DbContainer.GetConnectionString())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options);
}
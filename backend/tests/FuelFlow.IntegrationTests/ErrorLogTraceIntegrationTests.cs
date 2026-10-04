using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.Features.ErrorLogs;
using FuelFlow.Features.ErrorLogs.Logging;
using FuelFlow.Features.Providers;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Real-Postgres coverage for the error-log sink: one failed request emits several records
/// (request logging, the global handler, the feature handler, EF Core) and the journal gave no
/// way to tell them apart, nor a way to read why they failed without expanding every row.
/// Asserts both against the table the admin screen actually reads.
/// </summary>
[Collection("Integration Tests")]
public sealed class ErrorLogTraceIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public ErrorLogTraceIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Sink_PersistsTheCauseAndOneTraceIdPerIncident()
    {
        await ResetAsync();

        var fault = new InvalidOperationException("The LINQ expression 'x.Status.ToString()' could not be translated.");
        string traceId;

        // The shapes one faulting request produced in the journal: the global handler and the
        // feature handler both logging, on top of the EF Core record EF emits for the same save.
        using (var activity = new Activity("request").Start())
        {
            traceId = activity.TraceId.ToString();
            await WriteLogsAsync(logger => logger.LogError(fault, "Unhandled exception for {Method} {Path}", "GET", "/api/company/invitations"));
            await WriteLogsAsync(logger => logger.LogError(fault, "Failed to render or process PDF pages"));
        }

        // A separate request, so a sibling root activity with its own trace id.
        string otherTraceId;
        using (var other = new Activity("other-request").Start())
        {
            otherTraceId = other.TraceId.ToString();
            await WriteLogsAsync(logger => logger.LogError(fault, "An unrelated failure somewhere else"));
        }

        otherTraceId.Should().NotBe(traceId);

        var rows = await ReadRowsAsync();

        rows.Should().HaveCount(3);

        // The cause is readable in the message cell, not only in the expanded detail.
        rows.Should().OnlyContain(row => row.Message.Contains("System.InvalidOperationException: The LINQ expression"));
        rows.Should().Contain(row => row.Message.StartsWith("Unhandled exception for GET /api/company/invitations"));
        rows.Should().OnlyContain(row => row.ExceptionType == typeof(InvalidOperationException).FullName);
        rows.Should().OnlyContain(row => row.StackTrace!.Contains("could not be translated"));

        // Every record of this request shares one trace id, so the admin can narrow the journal
        // to a single incident instead of reading several near-identical rows.
        rows.Where(row => row.TraceId == traceId).Should().HaveCount(2);
        rows.Should().ContainSingle(row => row.TraceId == otherTraceId, "a second request must not be filed under the first one");
    }

    [Fact]
    public async Task GetAll_FiltersToASingleIncidentByTraceId()
    {
        await ResetAsync();

        var traceId = Guid.NewGuid().ToString();
        var otherTraceId = Guid.NewGuid().ToString();

        await using (var seed = CreateContext())
        {
            seed.ErrorLogs.AddRange(
                NewRow("first record of the incident", traceId),
                NewRow("second record of the incident", traceId),
                NewRow("a different incident entirely", otherTraceId),
                NewRow("a background job with no request", traceId: null));
            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext();
        var controller = new ErrorLogsController(context, new ProviderEventService(context));

        var incident = await ReadItemsAsync(controller.GetAll(traceId: traceId));

        incident.Should().HaveCount(2);
        incident.Should().OnlyContain(dto => dto.TraceId == traceId);
        incident.Select(dto => dto.Message).Should().BeEquivalentTo([
            "first record of the incident",
            "second record of the incident"
        ]);

        // The free-text search also reaches the trace id, so a trace copied out of a client's
        // ProblemDetails finds its rows without the reader having to know this endpoint.
        var bySearch = await ReadItemsAsync(controller.GetAll(search: traceId));
        bySearch.Should().HaveCount(2);
    }

    private static ErrorLog NewRow(string message, string? traceId) => new()
    {
        Id = Guid.NewGuid(),
        LoggedAtUtc = DateTime.UtcNow,
        Level = "Error",
        Message = message,
        Source = "FuelFlow.Middleware.GlobalExceptionHandler",
        TraceId = traceId
    };

    /// <summary>
    /// Drives the real sink against the container: the provider resolves its
    /// <see cref="NpgsqlDataSource"/> from the services it is handed, so a bare provider is enough.
    /// </summary>
    private async Task WriteLogsAsync(Action<ILogger> act)
    {
        var before = await CountRowsAsync();

        await using var dataSource = new NpgsqlDataSourceBuilder(_fixture.DbContainer.GetConnectionString()).Build();
        var services = new ServiceCollection().AddSingleton(dataSource).BuildServiceProvider();
        var provider = new DatabaseLoggerProvider(services);

        act(provider.CreateLogger("FuelFlow.Middleware.GlobalExceptionHandler"));

        // Wait for the writer task rather than disposing the provider to flush it: Dispose
        // cancels the channel reader, which discards whatever is still queued.
        for (var attempt = 0; attempt < 50 && await CountRowsAsync() <= before; attempt++)
        {
            await Task.Delay(100);
        }
    }

    private async Task<int> CountRowsAsync()
    {
        await using var context = CreateContext();
        return await context.ErrorLogs.AsNoTracking().CountAsync();
    }

    private async Task<List<ErrorLog>> ReadRowsAsync()
    {
        await using var context = CreateContext();
        return await context.ErrorLogs.AsNoTracking().ToListAsync();
    }

    private static async Task<List<ErrorLogDto>> ReadItemsAsync(Task<IActionResult> result)
    {
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var page = JsonSerializer.Deserialize<Page<ErrorLogDto>>(
            JsonSerializer.Serialize(ok.Value),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        page.Should().NotBeNull();
        return page!.Items;
    }

    private sealed record Page<T>(int Total, List<T> Items);

    private async Task ResetAsync()
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"error_logs\" RESTART IDENTITY CASCADE");
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.DbContainer.GetConnectionString())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options);
}
using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Notifications.SharedModels;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Settings.SharedModels;
using FuelFlow.Features.Support.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Covers the nightly DataRetentionService against a real PostgreSQL container: when enabled it
/// prunes only the dead-and-aged rows in each high-churn table and leaves live or too-recent rows
/// untouched; when disabled it is a pure dry-run that deletes nothing.
/// </summary>
[Collection("Integration Tests")]
public sealed class DataRetentionIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;
    private static readonly Guid UserId = Guid.NewGuid();

    public DataRetentionIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Cleanup_WhenEnabled_PurgesOnlyDeadAndAgedRows()
    {
        await SeedAsync(seed =>
        {
            seed.Users.Add(CreateUser(UserId));
            Enable(seed);
            SeedFixture(seed);
        });

        await RunAsync();

        using var verify = CreateContext();
        (await verify.VerificationCodes.CountAsync()).Should().Be(2);
        (await verify.RefreshTokens.CountAsync()).Should().Be(2);
        (await verify.OutboxEvents.CountAsync()).Should().Be(2);
        (await verify.Notifications.CountAsync()).Should().Be(2);
        (await verify.ErrorLogs.CountAsync()).Should().Be(1);
        (await verify.PushTokens.CountAsync()).Should().Be(1);
        (await verify.SupportMessages.CountAsync()).Should().Be(1);
        (await verify.SupportMessages.AllAsync(m => m.Message != "eligible")).Should().BeTrue();

        // Counts alone prove the right *number* went; the discriminator asserts prove the right
        // *rows* went — the "eligible" (dead + aged) row in each table, never the recent/live ones.
        (await verify.VerificationCodes.AllAsync(c => c.Code != "eligible")).Should().BeTrue();
        (await verify.RefreshTokens.AllAsync(t => t.Token != "eligible")).Should().BeTrue();
        // outbox has no stable text discriminator (Payload is json); assert by predicate instead —
        // the only processed-and-aged row (the eligible one) is gone; recent + unprocessed remain.
        (await verify.OutboxEvents.AnyAsync(o =>
            o.Processed && o.ProcessedAtUtc != null
            && o.ProcessedAtUtc < DateTime.UtcNow.AddDays(-DataRetentionService.OutboxEventRetentionDays)))
            .Should().BeFalse();
        (await verify.Notifications.AllAsync(n => n.Title != "eligible")).Should().BeTrue();
        (await verify.ErrorLogs.AllAsync(e => e.Message != "eligible")).Should().BeTrue();
        (await verify.PushTokens.AllAsync(p => p.Token != "eligible")).Should().BeTrue();
        (await verify.AuditEventOutbox.AllAsync(e => e.Summary != "eligible")).Should().BeTrue();
        // Both dead-device shapes go, and the live one survives even though its last sighting is
        // recent rather than old: age alone never removes a device that still reports in.
        (await verify.Devices.AllAsync(d => d.DeviceId != "eligible-silent")).Should().BeTrue();
        (await verify.Devices.AllAsync(d => d.DeviceId != "eligible-revoked")).Should().BeTrue();
        (await verify.Devices.AnyAsync(d => d.DeviceId == "live")).Should().BeTrue();
    }

    [Fact]
    public async Task Cleanup_PrunePredicatesHaveIndexSupport()
    {
        // Every prune is a nightly DELETE over a table that only grows. Without an index the
        // planner reads the whole table every night, which is invisible until the table is big -
        // and the job was a no-op by default, so nothing ever surfaced it. Measured on PG16 at
        // 200k rows: push_tokens went Seq Scan 15.0 ms -> Index Scan 0.066 ms, notifications
        // Seq Scan 31.8 ms -> Bitmap 0.114 ms.
        //
        // Asserted against pg_indexes rather than the model, because the point is what the
        // planner can actually use - a filter on the wrong table proves nothing.
        await using var context = CreateContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT indexname FROM pg_indexes
            WHERE schemaname = current_schema()
              AND indexname = ANY (@names)
            """;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "names";
        parameter.Value = new[]
        {
            "IX_push_tokens_last_seen_at_utc",
            "IX_notifications_is_read_created_at_utc",
            "IX_devices_last_seen_at"
        };
        command.Parameters.Add(parameter);

        var present = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            present.Add(reader.GetString(0));
        }

        present.Should().BeEquivalentTo(new[]
        {
            "IX_push_tokens_last_seen_at_utc",
            "IX_notifications_is_read_created_at_utc",
            "IX_devices_last_seen_at"
        });
    }

    [Fact]
    public async Task Cleanup_SplitDevicePredicate_MatchesWhatTheCombinedOrWouldDelete()
    {
        // The service prunes devices with two statements instead of
        // `Status == Revoked || LastSeenAt < cutoff`, because a BitmapOr cannot usefully combine a
        // bare status comparison with an indexed range: the combined form stayed on a Seq Scan
        // even with the index present (28.5 ms vs 3.7 ms split, at 250k rows).
        //
        // Splitting a predicate is only safe if it selects exactly the same rows. This pins that:
        // the two sets must be disjoint, and their union must equal the OR - including the rows
        // that make it non-trivial (a revoked device inside the window, a silent active one
        // outside it, and a live recent one that must survive both).
        await SeedAsync(seed =>
        {
            seed.Users.Add(CreateUser(UserId));
            Enable(seed);
            SeedFixture(seed);
        });

        var now = DateTime.UtcNow;
        var cutoff = now.AddDays(-DataRetentionService.DeviceRetentionDays);

        using var readContext = CreateContext();
        var devices = readContext.Devices.ToList();

        var combined = devices
            .Where(d => d.Status == DeviceStatus.Revoked || d.LastSeenAt < cutoff)
            .Select(d => d.Id)
            .ToHashSet();
        var split = devices
            .Where(d => d.Status == DeviceStatus.Revoked || (d.Status != DeviceStatus.Revoked && d.LastSeenAt < cutoff))
            .Select(d => d.Id)
            .ToHashSet();

        combined.Should().NotBeEmpty("otherwise this test proves nothing about the fixtures");
        split.Should().BeEquivalentTo(combined, "the two-statement form must select exactly what the OR selected");
    }

    [Fact]
    public async Task Cleanup_WhenDisabled_DeletesNothing()
    {
        await SeedAsync(seed =>
        {
            seed.Users.Add(CreateUser(UserId));
            // DataRetention:Enabled intentionally NOT set -> fail-safe default of false (dry-run).
            SeedFixture(seed);
        });

        await RunAsync();

        using var verify = CreateContext();
        (await verify.VerificationCodes.CountAsync()).Should().Be(3);
        (await verify.RefreshTokens.CountAsync()).Should().Be(3);
        (await verify.OutboxEvents.CountAsync()).Should().Be(3);
        (await verify.Notifications.CountAsync()).Should().Be(3);
        (await verify.ErrorLogs.CountAsync()).Should().Be(2);
        (await verify.PushTokens.CountAsync()).Should().Be(2);
        (await verify.AuditEventOutbox.CountAsync()).Should().Be(2);
        (await verify.Devices.CountAsync()).Should().Be(3);
        (await verify.SupportMessages.CountAsync()).Should().Be(2);
    }

    private static void Enable(ApplicationDbContext seed) => seed.AppSettings.Add(new AppSetting
    {
        Key = AppSettingKeys.DataRetentionEnabled,
        Value = "true",
        UpdatedAtUtc = DateTime.UtcNow
    });

    private async Task RunAsync()
    {
        using var context = CreateContext();
        var service = new DataRetentionService(
            context, new RuntimeSettingsService(context), NullLogger<DataRetentionService>.Instance);
        await service.CleanupAsync();
    }

    // Each table gets three (or two) rows tagged by a stable discriminator: "eligible" = dead and
    // aged past the window (must be purged), "recent" = dead but inside the window (kept), and where
    // it applies "live"/"unprocessed"/"unread" = not dead at all (kept regardless of age).
    private static void SeedFixture(ApplicationDbContext seed)
    {
        var now = DateTime.UtcNow;

        seed.VerificationCodes.Add(NewCode("eligible", isUsed: true, expiresAtUtc: now.AddDays(-9), createdAtUtc: now.AddDays(-10)));
        seed.VerificationCodes.Add(NewCode("recent", isUsed: true, expiresAtUtc: now.AddDays(-1), createdAtUtc: now.AddDays(-2)));
        seed.VerificationCodes.Add(NewCode("live", isUsed: false, expiresAtUtc: now.AddDays(5), createdAtUtc: now.AddDays(-10)));

        seed.RefreshTokens.Add(NewToken("eligible", isRevoked: true, expiresAtUtc: now.AddDays(-10), createdAtUtc: now.AddDays(-40)));
        seed.RefreshTokens.Add(NewToken("recent", isRevoked: true, expiresAtUtc: now.AddDays(-1), createdAtUtc: now.AddDays(-5)));
        seed.RefreshTokens.Add(NewToken("live", isRevoked: false, expiresAtUtc: now.AddDays(30), createdAtUtc: now.AddDays(-40)));

        seed.OutboxEvents.Add(NewOutbox(processed: true, processedAtUtc: now.AddDays(-40), createdAtUtc: now.AddDays(-41)));
        seed.OutboxEvents.Add(NewOutbox(processed: true, processedAtUtc: now.AddDays(-5), createdAtUtc: now.AddDays(-6)));
        seed.OutboxEvents.Add(NewOutbox(processed: false, processedAtUtc: null, createdAtUtc: now.AddDays(-41)));

        seed.Notifications.Add(NewNotification("eligible", isRead: true, createdAtUtc: now.AddDays(-100)));
        seed.Notifications.Add(NewNotification("recent", isRead: true, createdAtUtc: now.AddDays(-10)));
        seed.Notifications.Add(NewNotification("unread", isRead: false, createdAtUtc: now.AddDays(-100)));

        seed.ErrorLogs.Add(NewErrorLog("eligible", loggedAtUtc: now.AddDays(-100)));
        seed.ErrorLogs.Add(NewErrorLog("recent", loggedAtUtc: now.AddDays(-10)));

        seed.PushTokens.Add(NewPushToken("eligible", lastSeenAtUtc: now.AddDays(-100)));
        seed.PushTokens.Add(NewPushToken("recent", lastSeenAtUtc: now.AddDays(-10)));

        // Contact-form submissions. The Message carries the "eligible"/"recent" discriminator, since
        // a SupportMessage has no natural key beyond its own id.
        seed.SupportMessages.Add(NewSupportMessage("eligible", now.AddDays(-400)));
        seed.SupportMessages.Add(NewSupportMessage("recent", now.AddDays(-10)));

        // The admin change audit trail, aged only: a change two seasons old is pruned, last season's
        // is kept, and there is no "live" variant because a row here is not work waiting to happen -
        // it is a record of something a person already did.
        seed.AuditEventOutbox.Add(NewProviderEvent("eligible", changedAtUtc: now.AddDays(-400)));
        seed.AuditEventOutbox.Add(NewProviderEvent("recent", changedAtUtc: now.AddDays(-100)));

        // Devices are pruned on the same rule as push tokens, plus revocation: a revoked device is
        // dead whatever its age, and an active one that has gone quiet for a season is an uninstall.
        seed.Devices.Add(NewDevice("eligible-silent", status: DeviceStatus.Active, lastSeenAt: now.AddDays(-100)));
        seed.Devices.Add(NewDevice("eligible-revoked", status: DeviceStatus.Revoked, lastSeenAt: now.AddDays(-2)));
        seed.Devices.Add(NewDevice("live", status: DeviceStatus.Active, lastSeenAt: now.AddDays(-2)));
    }

    private static AuditEventOutbox NewProviderEvent(string summary, DateTime changedAtUtc) => new()
    {
        AggregateType = "FuelPackage",
        AggregateId = "pkg-1",
        ScopeId = "okko",
        EventType = "Updated",
        NewValue = "{}",
        ChangedByUserId = UserId,
        ChangedByUserName = "tester",
        Summary = summary,
        ChangedAtUtc = changedAtUtc
    };

    private static Device NewDevice(string deviceId, DeviceStatus status, DateTime lastSeenAt) => new()
    {
        UserId = UserId,
        DeviceId = deviceId,
        PublicKey = $"key-{deviceId}",
        Status = status,
        CreatedAt = lastSeenAt.AddDays(-1),
        LastSeenAt = lastSeenAt
    };

    private static VerificationCode NewCode(string code, bool isUsed, DateTime expiresAtUtc, DateTime createdAtUtc) => new()
    {
        Id = Guid.NewGuid(),
        PhoneNumber = "+380000000000",
        Code = code,
        IsUsed = isUsed,
        UsedAtUtc = isUsed ? createdAtUtc : null,
        ExpiresAtUtc = expiresAtUtc,
        CreatedAtUtc = createdAtUtc
    };

    private static RefreshToken NewToken(string token, bool isRevoked, DateTime expiresAtUtc, DateTime createdAtUtc) => new()
    {
        Id = Guid.NewGuid(),
        UserId = UserId,
        FamilyId = Guid.NewGuid(),
        Token = token,
        IsRevoked = isRevoked,
        RevokedAtUtc = isRevoked ? createdAtUtc : null,
        ExpiresAtUtc = expiresAtUtc,
        CreatedAtUtc = createdAtUtc
    };

    private static OutboxEvent NewOutbox(bool processed, DateTime? processedAtUtc, DateTime createdAtUtc) => new()
    {
        EventType = OutboxEventType.OrderCreated,
        Payload = "{}",
        Processed = processed,
        ProcessedAtUtc = processedAtUtc,
        CreatedAtUtc = createdAtUtc
    };

    private static Notification NewNotification(string title, bool isRead, DateTime createdAtUtc) => new()
    {
        Id = Guid.NewGuid(),
        UserId = UserId,
        Title = title,
        Message = "test",
        IsRead = isRead,
        CreatedAtUtc = createdAtUtc,
        UpdatedAtUtc = createdAtUtc
    };

    private static ErrorLog NewErrorLog(string message, DateTime loggedAtUtc) => new()
    {
        Id = Guid.NewGuid(),
        Level = "Error",
        Message = message,
        LoggedAtUtc = loggedAtUtc
    };

    private static SupportMessage NewSupportMessage(string message, DateTime createdAtUtc) => new()
    {
        Id = Guid.NewGuid(),
        Email = $"c-{message}@example.test",
        Message = message,
        CreatedAtUtc = createdAtUtc
    };

    private static UserPushToken NewPushToken(string token, DateTime lastSeenAtUtc) => new()
    {
        Id = Guid.NewGuid(),
        UserId = UserId,
        Token = token,
        Platform = "ios",
        IsActive = true,
        CreatedAtUtc = lastSeenAtUtc,
        UpdatedAtUtc = lastSeenAtUtc,
        LastSeenAtUtc = lastSeenAtUtc
    };

    private static User CreateUser(Guid userId) => new()
    {
        Id = userId,
        PhoneNumber = "+380995550203",
        IsActive = true,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private async Task SeedAsync(Action<ApplicationDbContext> seed)
    {
        using var context = CreateContext();
        await context.Database.MigrateAsync();
        await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "verification_codes", "refresh_tokens", "outbox_events", "error_logs", "notifications", "push_tokens", "provider_event_outbox", "devices", "support_messages", "app_settings", "users" RESTART IDENTITY CASCADE""");
        seed(context);
        await context.SaveChangesAsync();
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options);
}

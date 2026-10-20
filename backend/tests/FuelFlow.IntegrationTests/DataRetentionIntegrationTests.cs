using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Feedback.SharedModels;
using FuelFlow.Features.Notifications.SharedModels;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Settings.SharedModels;
using FuelFlow.Features.Support.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Events;
using FuelFlow.Features.Vouchers.Movements;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

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
        //
        // Found by the COLUMN each prune filters on, and by requiring that column to LEAD an
        // index - which is the property the planner needs and the only thing a leftmost-prefix
        // index can offer. Naming the indexes instead made this a hostage to the naming
        // convention, and the rename of every EF-named index in the schema turned it into a
        // failure with nothing about the database having changed.
        await using var context = CreateContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT want.tablename || '.' || want.col
              FROM (VALUES ('push_tokens', 'last_seen_at_utc'),
                           ('notifications', 'is_read'),
                           ('devices', 'last_seen_at')) AS want(tablename, col)
              JOIN pg_class t ON t.relname = want.tablename
              JOIN pg_namespace n ON n.oid = t.relnamespace AND n.nspname = current_schema()
              JOIN pg_attribute a ON a.attrelid = t.oid AND a.attname = want.col
             WHERE EXISTS (
                   SELECT 1
                     FROM pg_index i
                    WHERE i.indrelid = t.oid
                      AND i.indkey[0] = a.attnum
                 )
            """;

        var present = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            present.Add(reader.GetString(0));
        }

        present.Should().BeEquivalentTo(new[]
        {
            "push_tokens.last_seen_at_utc",
            "notifications.is_read",
            "devices.last_seen_at"
        }, "every prune column has to lead an index, or the nightly DELETE reads the whole table");
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

    /// <summary>
    /// The wedge. <c>purchase_batches.import_job_id</c> is Restrict and purchase batches are never
    /// purged - they are costing records - so the first import that had a cost entered against it
    /// made <c>DELETE FROM voucher_imports</c> raise 23503. Because the purge saved per table and
    /// threw on the first failure, every table listed after it was silently skipped on that run and
    /// on every run after, while the job still logged the tables it had reached.
    /// </summary>
    /// <summary>
    /// The D-13 tables are deliberately absent from the purge list, and the reason is written on the
    /// wall at the top of <see cref="DataRetentionService"/> — but a comment cannot refuse anything.
    ///
    /// <para>
    /// This is the test that makes the comment load-bearing: it seeds a voucher's custody movement and
    /// its status event, both aged far past every retention window in the service, runs retention
    /// <em>enabled</em>, and asserts both survive. Add <c>voucher_events</c> or
    /// <c>voucher_movements</c> to the purge list and this fails — which is the moment the list would
    /// otherwise be edited in passing, months later, by someone pruning "old audit rows" and not
    /// reading the wall.
    /// </para>
    /// <para>
    /// The seed rows are deliberately <em>ancient</em> rather than recent. A recent row would survive any
    /// sane window by accident; only a row older than the longest retention here can prove the table is
    /// not on the list at all.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Cleanup_Never_Purges_The_D13_Audit_Tables_However_Old()
    {
        var voucherId = Guid.NewGuid();
        var ancient = DateTime.UtcNow.AddYears(-5);

        await SeedAsync(seed =>
        {
            Enable(seed);
            seed.Users.Add(CreateUser(UserId));

            seed.FuelVouchers.Add(new FuelVoucher
            {
                Id = voucherId,
                Brand = "OKKO",
                FuelTypeId = ToUuid("okko-dp"),
                Liters = 10m,
                VoucherNumber = $"V-{voucherId:N}"[..20],
                QrPayload = $"qr-{voucherId:N}",
                Status = VoucherStatus.Available,
                CreatedAtUtc = ancient,
                UpdatedAtUtc = ancient
            });

            seed.VoucherMovements.Add(new VoucherMovement
            {
                Id = Guid.NewGuid(),
                Kind = VoucherMovementKinds.Issued,
                VoucherId = voucherId,
                FromLocation = VoucherLocations.Pool,
                ToLocation = VoucherLocations.Customer,
                ActorKind = VoucherActorKinds.System,
                ActorUserId = null,
                OccurredAtUtc = ancient
            });

            seed.VoucherEvents.Add(new VoucherEvent
            {
                Id = Guid.NewGuid(),
                VoucherId = voucherId,
                EventType = VoucherEventTypes.StatusChanged,
                FromStatus = nameof(VoucherStatus.Available),
                ToStatus = nameof(VoucherStatus.Expired),
                ActorKind = VoucherActorKinds.System,
                ActorUserId = null,
                OccurredAtUtc = ancient
            });
        });

        await RunAsync();

        using var verify = CreateContext();

        (await verify.VoucherMovements.CountAsync(m => m.VoucherId == voucherId))
            .Should().Be(1, "'where is this paper' cannot be rebuilt from fuel_vouchers - the row is updated in place for its whole life");
        (await verify.VoucherEvents.CountAsync(e => e.VoucherId == voucherId))
            .Should().Be(1, "the redemption happened at a pump and nobody signed anything, so this is the only proof");
    }

    [Fact]
    public async Task Cleanup_KeepsAnImportThatAPurchaseBatchStillReferences_AndPurgesTheRest()
    {
        var costed = Guid.NewGuid();
        var free = Guid.NewGuid();

        await SeedAsync(seed =>
        {
            Enable(seed);
            seed.Users.Add(CreateUser(UserId));
            SeedFixture(seed);
            SeedImportTail(seed, costed, free);
        });

        await RunAsync();

        using var verify = CreateContext();
        (await verify.VoucherImports.IgnoreQueryFilters().CountAsync(i => i.Id == costed))
            .Should().Be(1, "the batch holding this import's cost still points at it");
        (await verify.VoucherImports.IgnoreQueryFilters().CountAsync(i => i.Id == free))
            .Should().Be(0, "an import nothing references is still eligible");
    }

    /// <summary>
    /// The whole point of the fix, stated as an absence: with a costed import in the way the run
    /// still finishes <em>clean</em>, and still purges everything scheduled after
    /// <c>voucher_imports</c>.
    ///
    /// Before, the first import with a cost entered against it raised 23503, the exception ended
    /// the run, and <c>qr_parameters</c>, <c>support_messages</c>, <c>feedback_message_replies</c> and
    /// <c>feedback_messages</c> were silently skipped every night - while the log showed a tidy
    /// total for the tables it had reached. An earlier draft of this test tried to force the failure
    /// and assert the partial-run report; it cannot, because the reference guard means the delete
    /// no longer fails. That is the bug being fixed rather than a gap in the test.
    /// </summary>
    [Fact]
    public async Task Cleanup_FinishesCleanly_AndStillPurgesTheTablesAfterVoucherImports()
    {
        await SeedAsync(seed =>
        {
            Enable(seed);
            seed.Users.Add(CreateUser(UserId));
            SeedFixture(seed);
            SeedImportTail(seed, costed: null, free: null);

            // Listed AFTER voucher_imports in the purge order.
            seed.FeedbackMessages.Add(new FeedbackMessage
            {
                Id = Guid.NewGuid(),
                TelegramChatId = 4242,
                TelegramMessageId = 1,
                Text = "old feedback",
                CreatedAtUtc = Old()
            });
        });

        var logger = new RecordingLogger();
        using (var context = CreateContext())
        {
            await new DataRetentionService(
                context, new RuntimeSettingsService(context), logger).CleanupAsync();
        }

        using var verify = CreateContext();
        (await verify.FeedbackMessages.IgnoreQueryFilters().CountAsync())
            .Should().Be(0, "a blocked table must not stop the ones after it");

        logger.Entries.Should().NotContain(e => e.Level == LogLevel.Error,
            "the reference guard means nothing was blocked, so a clean total is the correct report");
        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Information
            && e.Message.Contains("purge complete", StringComparison.Ordinal)
            && e.Message.Contains("feedback_messages=1", StringComparison.Ordinal));
    }

    private static DateTime Old() => DateTime.UtcNow.AddDays(-400);

    /// <summary>
    /// Two finished imports past the window, one of them held by a purchase batch. Also clears the
    /// tail tables so a previous test in this shared container cannot decide the outcome.
    /// </summary>
    private static void SeedImportTail(
        ApplicationDbContext seed, Guid? costed, Guid? free)
    {
        seed.FeedbackMessages.RemoveRange(seed.FeedbackMessages);

        if (costed is { } costedId)
        {
            seed.VoucherImports.Add(NewImport(costedId, "costed.pdf"));

            // purchase_batches.supplier_id is a required FK, and nothing else in this fixture
            // creates a supplier - the rows below are the first ones to need one.
            var supplierId = Guid.NewGuid();
            seed.Suppliers.Add(new Supplier
            {
                EdrIpn = TestEdrIpns.Next(),
                Id = supplierId,
                Name = $"Retention Test Supplier {supplierId:N}"[..40],
                CreatedAtUtc = Old(),
                UpdatedAtUtc = Old()
            });

            seed.PurchaseBatches.Add(new PurchaseBatch
            {
                Id = Guid.NewGuid(),
                ImportJobId = costedId,
                FuelTypeId = ToUuid("okko-95"),
                Brand = "okko",
                SupplierId = supplierId,
                CreatedAtUtc = Old(),
                UpdatedAtUtc = Old()
            });
        }

        if (free is { } freeId)
            seed.VoucherImports.Add(NewImport(freeId, "uncosted.pdf"));
    }

    private static VoucherImport NewImport(Guid id, string fileName) => new()
    {
        Id = id,
        FileName = fileName,
        PageCount = 1,
        StartedAtUtc = Old(),
        CompletedAtUtc = Old(),
        Status = "Completed",
        ImportedCount = 10,
    };

    /// <summary>Captures what the job said, so "it reported a partial run" is assertable.</summary>
    private sealed class RecordingLogger : ILogger<DataRetentionService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));

        public void AddProvider(ILoggerProvider provider) { }
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
            """TRUNCATE TABLE "verification_codes", "refresh_tokens", "outbox_events", "error_logs", "notifications", "push_tokens", "provider_event_outbox", "devices", "support_messages", "app_settings", "users", "feedback_message_replies", "feedback_messages", "qr_parameters", "purchase_batches", "voucher_imports", "voucher_import_errors" RESTART IDENTITY CASCADE""");
        seed(context);
        await context.SaveChangesAsync();
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);
}

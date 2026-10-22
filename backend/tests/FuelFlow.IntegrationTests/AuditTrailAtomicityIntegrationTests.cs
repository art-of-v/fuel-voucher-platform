using FluentAssertions;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Features.Vouchers.UpdateVoucher;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The audit row and the change it describes are one fact: they commit together, or neither does.
/// </summary>
/// <remarks>
/// <para>
/// This is the acceptance criterion of C-3 (planning #533). The audit service used to call its own
/// <c>SaveChangesAsync</c> while every caller saved its business change first, so the row and the
/// change were two transactions. A failure between them left a committed change with nothing
/// explaining it - on a platform that moves money and issues bearer instruments, an audit trail
/// that can miss a change is not an audit trail.
/// </para>
/// <para>
/// It runs against the real PostgreSQL on purpose: the point is the transaction, and the
/// transaction is the database's.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class AuditTrailAtomicityIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public AuditTrailAtomicityIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext(params IInterceptor[] interceptors)
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .AddInterceptors(interceptors)
            .Options, TestQrCipher.Instance);

    private static UpdateVoucherCommandHandler Handler(ApplicationDbContext ctx)
        => new(ctx, new AuditEventService(ctx));

    /// <summary>One imported voucher of a fuel the migrations already seed.</summary>
    private static async Task<Guid> SeedImportedVoucherAsync(ApplicationDbContext context, string suffix)
    {
        var import = new VoucherImport
        {
            Id = Guid.NewGuid(),
            FileName = $"audit-atomic-{suffix}.pdf",
            PageCount = 1,
            StartedAtUtc = TestClock.UtcNow,
            CompletedAtUtc = TestClock.UtcNow,
            Status = "Completed",
            ImportedCount = 1,
        };
        var supplier = new Supplier
        {
            EdrIpn = TestEdrIpns.Next(),
            Id = Guid.NewGuid(),
            Name = $"Atomic supplier {suffix}",
            IsActive = true,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow,
        };
        context.VoucherImports.Add(import);
        context.Suppliers.Add(supplier);
        context.FuelVouchers.Add(new FuelVoucher
        {
            Id = Guid.NewGuid(),
            ImportJobId = import.Id,
            SupplierId = supplier.Id,
            BrandId = BrandIds.ForSlug("okko"),
            Brand = "okko",
            FuelTypeId = ToUuid("okko-95"),
            Liters = 50,
            VoucherNumber = $"atomic-{suffix}",
            QrPayload = $"9015$2000$;{suffix}=x?",
            Status = VoucherStatus.Imported,
            SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow).AddDays(180),
            CustomerExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow).AddDays(180),
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow,
        });

        await context.SaveChangesAsync();
        return (await context.FuelVouchers
            .Where(v => v.VoucherNumber == $"atomic-{suffix}")
            .Select(v => v.Id)
            .SingleAsync());
    }

    private static UpdateVoucherCommand Deactivate(Guid voucherId, Guid adminId) =>
        new(voucherId, nameof(VoucherStatus.Deactivated), null, adminId, "Atomic Admin");

    [Fact]
    public async Task ASuccessfulChange_Always_Has_Its_Audit_Row()
    {
        using var setup = CreateContext();
        var voucherId = await SeedImportedVoucherAsync(setup, "ok");
        var adminId = Guid.NewGuid();

        using (var ctx = CreateContext())
        {
            var result = await Handler(ctx).HandleAsync(Deactivate(voucherId, adminId), CancellationToken.None);
            result.Success.Should().BeTrue();
        }

        using var verify = CreateContext();
        var voucher = await verify.FuelVouchers.SingleAsync(v => v.Id == voucherId);
        voucher.Status.Should().Be(VoucherStatus.Deactivated);

        var audit = await verify.Set<AuditEventOutbox>().AsNoTracking()
            .SingleAsync(e => e.AggregateId == voucherId.ToString());

        audit.EventType.Should().Be("VoucherUpdated");
        audit.ChangedByUserId.Should().Be(adminId);
        audit.ChangedByUserName.Should().Be("Atomic Admin");
    }

    [Fact]
    public async Task An_Audit_Write_That_Fails_Rolls_The_Change_Back_With_It()
    {
        using var setup = CreateContext();
        var voucherId = await SeedImportedVoucherAsync(setup, "fail");
        var adminId = Guid.NewGuid();

        using (var ctx = CreateContext(new FailOnAuditInsert()))
        {
            var act = async () => await Handler(ctx).HandleAsync(Deactivate(voucherId, adminId), CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>("the audit row's insert failed");
        }

        using var verify = CreateContext();
        var voucher = await verify.FuelVouchers.SingleAsync(v => v.Id == voucherId);

        // The whole point: before the fix the business write was committed first and the audit
        // write was a second transaction, so this state - a changed voucher with no row naming who
        // changed it - was reachable.
        voucher.Status.Should().Be(VoucherStatus.Imported,
            "the change and its audit row are one fact: the audit write failed, so the change must not be committed");

        (await verify.Set<AuditEventOutbox>().CountAsync(e => e.AggregateId == voucherId.ToString()))
            .Should().Be(0, "the row that failed to be written must not be half-written either");
    }

    [Fact]
    public async Task The_Audit_Row_Is_Written_By_The_Same_Save_As_The_Change()
    {
        using var setup = CreateContext();
        var voucherId = await SeedImportedVoucherAsync(setup, "same-save");
        var adminId = Guid.NewGuid();

        var batches = new RecordSaveBatches();
        using (var ctx = CreateContext(batches))
        {
            var result = await Handler(ctx).HandleAsync(Deactivate(voucherId, adminId), CancellationToken.None);
            result.Success.Should().BeTrue();
        }

        var withAudit = batches.Batches.Where(b => b.HasAuditRow).ToList();

        withAudit.Should().ContainSingle(
            "the audit row is committed by the caller's own save - one save, not one per row. The saves "
            + "this handler made: " + string.Join(" | ", batches.Batches.Select(b => b.Names)));

        withAudit[0].HasBusinessChange.Should().BeTrue(
            "the save that carries the audit row must carry the change it describes, or the row is "
            + "a record of something that happened in a different transaction");
    }

    /// <summary>What one SaveChanges carried.</summary>
    private sealed record SaveBatch(bool HasAuditRow, bool HasBusinessChange, string Names);

    /// <summary>
    /// Notes what each SaveChanges carried, so the test can assert that the audit row and the
    /// change travelled in the same one.
    /// </summary>
    private sealed class RecordSaveBatches : SaveChangesInterceptor
    {
        public List<SaveBatch> Batches { get; } = new();

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
            => Record(eventData, result);

        // The async path is its own hook: EF Core 10 calls this one for SaveChangesAsync, and the
        // synchronous override is not consulted there.
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Record(eventData, result));

        private InterceptionResult<int> Record(DbContextEventData eventData, InterceptionResult<int> result)
        {
            var entries = eventData.Context!.ChangeTracker.Entries().ToList();

            Batches.Add(new SaveBatch(
                HasAuditRow: entries.Any(e => e.Entity is AuditEventOutbox),
                HasBusinessChange: entries.Any(e => e.Entity is not AuditEventOutbox && e.State != EntityState.Unchanged),
                Names: string.Join(",", entries.Select(e => e.Entity.GetType().Name + ":" + e.State))));

            return result;
        }
    }

    /// <summary>
    /// Fails the write of an audit row, and nothing else - the failure the old two-transaction
    /// shape could not survive: the business change was already committed by then.
    /// </summary>
    private sealed class FailOnAuditInsert : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
            => Fail(eventData, result);

        // The async path is its own hook: EF Core 10 calls this one for SaveChangesAsync, and the
        // synchronous override is not consulted there.
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Fail(eventData, result));

        private static InterceptionResult<int> Fail(DbContextEventData eventData, InterceptionResult<int> result)
        {
            var carriesAuditRow = eventData.Context!.ChangeTracker.Entries()
                .Any(e => e.Entity is AuditEventOutbox && e.State == EntityState.Added);

            if (carriesAuditRow)
            {
                throw new InvalidOperationException("the audit row's insert failed");
            }

            return result;
        }
    }
}

using FuelFlow.Features.Pricing;
using FluentAssertions;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.BulkActionVouchers;
using FuelFlow.Features.Vouchers.Exchange;
using FuelFlow.Features.Vouchers.PurchaseBatchCost;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Moq;
using Npgsql;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// #319 / D-13, stage 1: <c>voucher_exchanges.exchange_batch_id</c> pointed at nothing since
/// 2026-10-01. These tests are the definition of done the issue wrote down, one per bullet.
/// </summary>
/// <remarks>
/// The bug is not visible in a unit test because nothing was broken — the column held a real uuid, the
/// index was real, and every reader reconstructed what it needed by folding over the member rows. It
/// is only visible when the database is asked whether the reference resolves, which is what this class
/// is for.
/// </remarks>
public sealed class MovementBatchIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public MovementBatchIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task An_Exchange_Cannot_Reference_A_Batch_That_Does_Not_Exist()
    {
        // The whole point of the stage. Before it, this insert succeeded and left a row naming
        // something the database had never heard of.
        await using var seed = CreateContext();
        await seed.Database.MigrateAsync();
        await ResetAsync(seed);

        var supplierId = await SeedSupplierAsync(seed);
        var oldVoucherId = await SeedVoucherAsync(seed, "OLD-" + Guid.NewGuid().ToString("N")[..8]);

        seed.VoucherExchanges.Add(new VoucherExchange
        {
            Id = Guid.NewGuid(),
            ExchangeBatchId = Guid.NewGuid(),
            OldVoucherId = oldVoucherId,
            FuelTypeId = ToUuid("okko-95"),
            Brand = "OKKO",
            SupplierId = supplierId,
            SurchargeUah = 10m,
            ActingUserId = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow
        });

        var act = async () => await seed.SaveChangesAsync();
        (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task Confirming_An_Exchange_Writes_The_Settlement_Its_Pairs_Point_At()
    {
        // The handler used to mint the batch id in-process and never persist anything about the
        // settlement, so "what did we agree, and with whom" was reconstructible only from the pairs.
        await using var seed = CreateContext();
        await seed.Database.MigrateAsync();
        await ResetAsync(seed);

        var actingUserId = Guid.NewGuid();
        var supplierId = await SeedSupplierAsync(seed);
        var importId = await SeedImportAsync(seed);
        var expiry = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(6);

        // The ids come back from the seeder: a voucher seeded under an id the command does not name is
        // a voucher the handler never finds.
        var old1 = await SeedVoucherAsync(seed, "OLD-" + Guid.NewGuid().ToString("N")[..8],
            liters: 50m, expiry: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2),
            status: VoucherStatus.Available, supplierId: supplierId, costPerLiter: 30m);
        var old2 = await SeedVoucherAsync(seed, "OLD-" + Guid.NewGuid().ToString("N")[..8],
            liters: 50m, expiry: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2),
            status: VoucherStatus.Available, supplierId: supplierId, costPerLiter: 30m);
        var new1 = await SeedVoucherAsync(seed, "NEW-" + Guid.NewGuid().ToString("N")[..8],
            liters: 50m, expiry: expiry, status: VoucherStatus.Imported,
            importId: importId, supplierId: supplierId);
        var new2 = await SeedVoucherAsync(seed, "NEW-" + Guid.NewGuid().ToString("N")[..8],
            liters: 50m, expiry: expiry, status: VoucherStatus.Imported,
            importId: importId, supplierId: supplierId);
        await seed.SaveChangesAsync();

        await using (var ctx = CreateContext())
        {
            var result = await BuildHandler(ctx).HandleAsync(new ConfirmVoucherExchangeCommand(
                OldVoucherIds: [old1, old2],
                NewImportId: importId,
                Costs: [new ExchangeFuelCost(ToUuid("okko-95"), 30m)],
                SurchargeUah: 200m,
                ActingUserId: actingUserId,
                ActingUserName: "Operator Nova"));
            result.Success.Should().BeTrue(result.Error);
        }

        await using var verify = CreateContext();
        var pairs = await verify.VoucherExchanges.AsNoTracking().ToListAsync();
        pairs.Should().HaveCount(2);
        pairs.Should().OnlyContain(e => e.ExchangeBatchId != Guid.Empty);

        var batch = await verify.MovementBatches.AsNoTracking().SingleAsync();
        batch.Id.Should().Be(pairs[0].ExchangeBatchId,
            "the settlement is the thing the pairs point at, not a separately invented id");
        batch.Kind.Should().Be(MovementBatchKinds.VoucherExchange);
        batch.Status.Should().Be(MovementBatchStatuses.Confirmed);
        batch.SupplierId.Should().Be(supplierId);
        batch.OpenedByUserId.Should().Be(actingUserId);
        batch.OpenedByUserName.Should().Be("Operator Nova");
        batch.ConfirmedAtUtc.Should().NotBeNull("the CHECK pairs status with the timestamp");
        batch.SurchargeUah.Should().Be(200m, "the brand charged 200 for the settlement");
    }

    [Fact]
    public async Task The_Batch_Drill_Down_Reads_Its_Header_From_The_Settlement_Not_From_Its_Pairs()
    {
        // This is the behaviour change, and it needs a test that fails if the reader goes back to
        // folding. The settlement row is seeded with values the member rows do NOT agree with, so the
        // two sources cannot be confused for one another.
        await using var seed = CreateContext();
        await seed.Database.MigrateAsync();
        await ResetAsync(seed);

        var supplierId = await SeedSupplierAsync(seed);
        var batchId = Guid.NewGuid();
        var openedAt = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        var pairAt = new DateTime(2026, 10, 5, 17, 30, 0, DateTimeKind.Utc);

        MovementBatchSeeder.AddForExchange(seed, batchId, supplierId, 777.77m, openedAt, "Settlement Owner");

        foreach (var tag in new[] { "A", "B" })
        {
            var voucherId = await SeedVoucherAsync(seed, $"OLD-{tag}-{batchId:N}"[..16],
                liters: 10m, status: VoucherStatus.Available, supplierId: supplierId, costPerLiter: 30m);
            seed.VoucherExchanges.Add(new VoucherExchange
            {
                Id = Guid.NewGuid(),
                ExchangeBatchId = batchId,
                OldVoucherId = voucherId,
                FuelTypeId = ToUuid("okko-95"),
                Brand = "OKKO",
                SupplierId = supplierId,
                // Shares that add up to 30, deliberately NOT the settlement's 777.77.
                SurchargeUah = 15m,
                ActingUserId = Guid.NewGuid(),
                ActingUserName = "Pair Author",
                CreatedAtUtc = pairAt
            });
        }
        await seed.SaveChangesAsync();

        await using var ctx = CreateContext();
        var detail = await new GetVoucherExchangeBatchQueryHandler(ctx)
            .HandleAsync(new GetVoucherExchangeBatchQuery(batchId));

        detail.Should().NotBeNull();
        detail!.AtUtc.Should().Be(openedAt, "the settlement says when it was opened");
        detail.Status.Should().Be(MovementBatchStatuses.Confirmed);
        detail.RecordedBy.Should().Be("Settlement Owner");
        detail.TotalSurchargeUah.Should().Be(777.77m,
            "the settlement says what was charged, which the pair shares need not add up to");
        detail.Pairs.Should().HaveCount(2, "the per-pair evidence is still read from the rows");
    }

    [Fact]
    public async Task A_Batch_Outlives_Its_Pairs_Being_Deleted()
    {
        // RESTRICT, not CASCADE. A settlement is the record of what was agreed with a supplier, so a
        // cascade here would let an ordinary audit cleanup delete the only answer to "what did we
        // agree". The delete itself succeeds - the voucher-side RESTRICT guards the voucher, not the
        // exchange row - and the settlement has to still be there afterwards.
        await using var seed = CreateContext();
        await seed.Database.MigrateAsync();
        await ResetAsync(seed);

        var supplierId = await SeedSupplierAsync(seed);
        var batchId = Guid.NewGuid();
        var oldVoucherId = await SeedVoucherAsync(seed, "OLD-" + Guid.NewGuid().ToString("N")[..8]);

        MovementBatchSeeder.AddForExchange(seed, batchId, supplierId, 50m, DateTime.UtcNow);
        seed.VoucherExchanges.Add(new VoucherExchange
        {
            Id = Guid.NewGuid(),
            ExchangeBatchId = batchId,
            OldVoucherId = oldVoucherId,
            FuelTypeId = ToUuid("okko-95"),
            Brand = "OKKO",
            SupplierId = supplierId,
            SurchargeUah = 50m,
            ActingUserId = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow
        });
        await seed.SaveChangesAsync();

        seed.VoucherExchanges.RemoveRange(await seed.VoucherExchanges.ToListAsync());
        await seed.SaveChangesAsync();

        await using var verify = CreateContext();
        (await verify.VoucherExchanges.AsNoTracking().CountAsync()).Should().Be(0);
        (await verify.MovementBatches.AsNoTracking().AnyAsync(b => b.Id == batchId))
            .Should().BeTrue("the settlement survives its members - nothing cascaded from it");
    }

    [Fact]
    public async Task A_Batch_Must_Be_Created_Consistently()
    {
        // The CHECKs, as the database enforces them. A settlement that says confirmed and carries no
        // timestamp cannot answer "when", and a rejection with nothing to say why is not auditable.
        await using var seed = CreateContext();
        await seed.Database.MigrateAsync();
        await ResetAsync(seed);

        var supplierId = await SeedSupplierAsync(seed);

        await AssertRefused(ConfirmedWithoutTimestamp(supplierId));
        await AssertRefused(RejectedWithoutReason(supplierId));
        await AssertRefused(ExchangeWithoutSupplier(supplierId));
        await AssertRefused(UnknownKind(supplierId));
        await AssertRefused(NegativeSurcharge(supplierId));

        // And the shape the application actually writes is accepted.
        MovementBatchSeeder.AddForExchange(seed, Guid.NewGuid(), supplierId, 0m, DateTime.UtcNow,
            "Zero Is A Real Answer");
        var ok = async () => await seed.SaveChangesAsync();
        await ok.Should().NotThrowAsync("0 UAH is free compensation, not an invalid amount");
    }

    // ---- fixture builders for the invalid shapes ----------------------------------------------

    private static MovementBatch ConfirmedWithoutTimestamp(Guid supplierId) => new()
    {
        Id = Guid.NewGuid(), Kind = MovementBatchKinds.VoucherExchange, SupplierId = supplierId,
        Status = MovementBatchStatuses.Confirmed, SurchargeUah = 10m, OpenedByUserId = Guid.NewGuid(),
        OpenedAtUtc = DateTime.UtcNow, CreatedAtUtc = DateTime.UtcNow
        // ConfirmedAtUtc deliberately null.
    };

    private static MovementBatch RejectedWithoutReason(Guid supplierId) => new()
    {
        Id = Guid.NewGuid(), Kind = MovementBatchKinds.VoucherExchange, SupplierId = supplierId,
        Status = MovementBatchStatuses.Rejected, SurchargeUah = 10m, OpenedByUserId = Guid.NewGuid(),
        OpenedAtUtc = DateTime.UtcNow, CreatedAtUtc = DateTime.UtcNow
        // RejectionReason deliberately null.
    };

    private static MovementBatch ExchangeWithoutSupplier(Guid supplierId) => new()
    {
        Id = Guid.NewGuid(), Kind = MovementBatchKinds.VoucherExchange,
        Status = MovementBatchStatuses.Confirmed, SurchargeUah = 10m, OpenedByUserId = Guid.NewGuid(),
        OpenedAtUtc = DateTime.UtcNow, ConfirmedAtUtc = DateTime.UtcNow,
        CreatedAtUtc = DateTime.UtcNow
        // SupplierId deliberately null on a voucher_exchange kind.
    };

    private static MovementBatch UnknownKind(Guid supplierId) => new()
    {
        Id = Guid.NewGuid(), Kind = "voucher_reissue", SupplierId = supplierId,
        Status = MovementBatchStatuses.Confirmed, SurchargeUah = 10m, OpenedByUserId = Guid.NewGuid(),
        OpenedAtUtc = DateTime.UtcNow, ConfirmedAtUtc = DateTime.UtcNow, CreatedAtUtc = DateTime.UtcNow
    };

    private static MovementBatch NegativeSurcharge(Guid supplierId) => new()
    {
        Id = Guid.NewGuid(), Kind = MovementBatchKinds.VoucherExchange, SupplierId = supplierId,
        Status = MovementBatchStatuses.Confirmed, SurchargeUah = -1m, OpenedByUserId = Guid.NewGuid(),
        OpenedAtUtc = DateTime.UtcNow, ConfirmedAtUtc = DateTime.UtcNow, CreatedAtUtc = DateTime.UtcNow
    };

    private async Task AssertRefused(MovementBatch batch)
    {
        await using var seed = CreateContext();
        seed.MovementBatches.Add(batch);
        var act = async () => await seed.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>(
            $"the database must refuse a batch shaped like this one (id {batch.Id:N})");
    }

    // ---- harness ------------------------------------------------------------------------------

    private static ConfirmVoucherExchangeCommandHandler BuildHandler(ApplicationDbContext ctx)
    {
        var eventService = new AuditEventService(ctx);
        var backgroundJobClient = new Mock<IBackgroundJobClient>();
        backgroundJobClient.Setup(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>())).Returns("job-id");
        var bulkAction = new BulkActionVouchersCommandHandler(ctx, backgroundJobClient.Object, eventService);

        return new ConfirmVoucherExchangeCommandHandler(
            ctx, new BlendedCostRecalculator(ctx, new PriceHistoryRecorder(ctx)), bulkAction, eventService);
    }

    private static async Task<Guid> SeedSupplierAsync(ApplicationDbContext ctx)
    {
        var id = Guid.NewGuid();
        ctx.Suppliers.Add(new Supplier
        {
            Id = id, Name = $"Seed {id:N}"[..20], IsActive = true,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> SeedImportAsync(ApplicationDbContext ctx)
    {
        var id = Guid.NewGuid();
        ctx.VoucherImports.Add(new VoucherImport
        {
            Id = id, FileName = $"import-{id:N}.pdf", Status = "Completed",
            StartedAtUtc = DateTime.UtcNow, CompletedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> SeedVoucherAsync(
        ApplicationDbContext ctx,
        string number,
        string fuelTypeId = "okko-95",
        decimal liters = 10m,
        DateOnly? expiry = null,
        VoucherStatus status = VoucherStatus.Available,
        Guid? importId = null,
        Guid? supplierId = null,
        decimal? costPerLiter = null)
    {
        var id = Guid.NewGuid();
        var when = expiry ?? DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1);
        ctx.FuelVouchers.Add(new FuelVoucher
        {
            Id = id,
            Brand = "OKKO",
            FuelTypeId =   ToUuid(fuelTypeId),
            Liters = liters,
            SupplierExpirationDate = when,
            CustomerExpirationDate = when,
            VoucherNumber = number,
            QrPayload = $"qr-{id:N}",
            Status = status,
            ImportJobId = importId,
            SupplierId = supplierId,
            CostPerLiter = costPerLiter,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        return id;
    }

    /// <summary>
    /// movement_batches first: it is the parent, and RESTRICT means the child cannot be truncated past
    /// it. CASCADE on the voucher/import edges is what lets one statement clear the rest.
    /// </summary>
    private static async Task ResetAsync(ApplicationDbContext ctx)
        => await ctx.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "voucher_exchanges", "movement_batches", "purchase_batches", "voucher_imports", "fulfillments", "orders", "order_line_items", "outbox_events", "payments", "fuel_vouchers", "suppliers", "users", "provider_event_outbox" RESTART IDENTITY CASCADE""");

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString + ";SSL Mode=Disable")
            .Options, TestQrCipher.Instance);
}
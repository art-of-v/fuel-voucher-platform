using FluentAssertions;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Events;
using FuelFlow.Features.Vouchers.Movements;
using FuelFlow.Features.Vouchers.Redemptions;
using FuelFlow.Features.Vouchers.RestoreVoucher;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The holder's undo, now that a report waits for evidence. <c>PendingReconciliation</c> goes back
/// to <c>Assigned</c>, the claim is withdrawn, and the provisional drafts are discarded.
/// <para>
/// A <c>Used</c> voucher is no longer restorable here: once the evidence is accepted the sale is
/// posted, and undoing it is an operator reversal.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public sealed class RestoreVoucherIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public RestoreVoucherIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RestoreVoucher_WithdrawsThePendingClaim_AndDiscardsTheDrafts()
    {
        var userId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();

        await SeedPendingVoucherAsync(userId, voucherId);

        await using (var verify = CreateContext())
        {
            (await verify.JournalEntries.CountAsync(e => e.SourceId == voucherId))
                .Should().Be(2, "the report composed cost and revenue drafts; the VAT was carved out at the receipt");
            (await verify.JournalEntries
                    .Where(e => e.SourceId == voucherId)
                    .Select(e => e.Status)
                    .ToListAsync())
                .Should().OnlyContain(status => status == JournalEntryStatus.Draft);
        }

        await using (var context = CreateContext())
        {
            var handler = new RestoreVoucherCommandHandler(context, TestRedemptions.For(context));
            var response = await handler.HandleAsync(new RestoreVoucherCommand(voucherId, userId));

            response.Success.Should().BeTrue();
            response.Message.Should().Be("Voucher restored to Assigned status");
        }

        await using var final = CreateContext();
        var stored = await final.FuelVouchers
            .Where(v => v.Id == voucherId)
            .Select(v => new { v.Status, v.UsedAtUtc, v.UpdatedAtUtc })
            .SingleAsync();

        stored.Status.Should().Be(VoucherStatus.Assigned);
        stored.UsedAtUtc.Should().BeNull();
        stored.UpdatedAtUtc.Should().BeCloseTo(TestClock.UtcNow, TimeSpan.FromSeconds(10));

        var redemption = await final.VoucherRedemptions
            .Where(r => r.VoucherId == voucherId)
            .SingleAsync();
        redemption.Status.Should().Be(VoucherRedemptionStatus.Withdrawn);
        redemption.ClosedByUserId.Should().Be(userId);

        (await final.JournalEntries.CountAsync(e => e.SourceId == voucherId))
            .Should().Be(0, "a withdrawn claim has no sale to post");
    }

    [Fact]
    public async Task RestoreVoucher_RecordsTheWithdrawal_NamingWhoDidIt()
    {
        var userId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();

        await SeedPendingVoucherAsync(userId, voucherId);

        await using (var context = CreateContext())
        {
            var handler = new RestoreVoucherCommandHandler(context, TestRedemptions.For(context));
            await handler.HandleAsync(new RestoreVoucherCommand(voucherId, userId));
        }

        await using var verify = CreateContext();
        var undo = await verify.VoucherEvents
            .AsNoTracking()
            .Where(e => e.VoucherId == voucherId
                && e.FromStatus == nameof(VoucherStatus.PendingReconciliation))
            .SingleAsync();

        undo.EventType.Should().Be(VoucherEventTypes.StatusChanged);
        undo.ToStatus.Should().Be(nameof(VoucherStatus.Assigned));
        undo.ActorKind.Should().Be(VoucherActorKinds.User);
        undo.ActorUserId.Should().Be(userId);
    }

    [Fact]
    public async Task RestoreVoucher_RefusesToTouchAnArchivedVoucher()
    {
        var userId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();

        await SeedPendingVoucherAsync(userId, voucherId, archived: true);

        await using var context = CreateContext();
        var handler = new RestoreVoucherCommandHandler(context, TestRedemptions.For(context));

        var response = await handler.HandleAsync(new RestoreVoucherCommand(voucherId, userId));

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be("NotFound");

        await using var verify = CreateContext();
        var stored = await verify.FuelVouchers
            .IgnoreQueryFilters()
            .Where(v => v.Id == voucherId)
            .Select(v => new { v.Status, v.IsDeleted })
            .SingleAsync();

        stored.Status.Should().Be(VoucherStatus.PendingReconciliation,
            "an archived voucher must come back untouched");
        stored.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task RestoreVoucher_RefusesAnotherCustomersVoucher()
    {
        var ownerId = Guid.NewGuid();
        var intruderId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();

        await SeedPendingVoucherAsync(ownerId, voucherId);

        await using var context = CreateContext();
        var handler = new RestoreVoucherCommandHandler(context, TestRedemptions.For(context));

        var response = await handler.HandleAsync(new RestoreVoucherCommand(voucherId, intruderId));

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be("Forbidden");

        await using var verify = CreateContext();
        var stored = await verify.FuelVouchers
            .Where(v => v.Id == voucherId)
            .Select(v => v.Status)
            .SingleAsync();

        stored.Should().Be(VoucherStatus.PendingReconciliation);

        (await verify.VoucherEvents.CountAsync(e => e.VoucherId == voucherId))
            .Should().Be(1, "a refusal records nothing beyond the report that already existed");
    }

    [Fact]
    public async Task RestoreVoucher_RefusesAConfirmedRedemption()
    {
        var userId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();

        await SeedUsedVoucherAsync(userId, voucherId);

        await using var context = CreateContext();
        var handler = new RestoreVoucherCommandHandler(context, TestRedemptions.For(context));

        var response = await handler.HandleAsync(new RestoreVoucherCommand(voucherId, userId));

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be("InvalidState");
        response.Message.Should().Contain("reversed by an operator");
    }

    private async Task SeedPendingVoucherAsync(Guid userId, Guid voucherId, bool archived = false)
    {
        await using (var seed = CreateContext())
        {
            await ResetDataAsync(seed);

            seed.Users.Add(new User
            {
                Id = userId,
                PhoneNumber = $"+38{userId:N}"[..20],
                IsActive = true,
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            });

            var orderId = SeedPurchaseOrder(seed, userId);
            var lineId = Guid.NewGuid();
            seed.OrderLineItems.Add(new OrderLineItem
            {
                Id = lineId,
                OrderId = orderId,
                BrandId = BrandIds.ForSlug("okko"),
                Brand = "okko",
                FuelTypeId = ToUuid("okko-95"),
                Liters = 50m,
                Quantity = 1,
                UnitPrice = 2500m,
                LineTotal = 2500m,
            });

            seed.FuelVouchers.Add(new FuelVoucher
            {
                Id = voucherId,
                BrandId = BrandIds.ForSlug("okko"),
                Brand = "okko",
                FuelTypeId = ToUuid("okko-95"),
                Liters = 50m,
                CostPerLiter = 45m,
                SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(1)),
                CustomerExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(1)),
                VoucherNumber = $"OKKO-{voucherId:N}"[..20],
                QrPayload = $"payload-{voucherId:N}",
                Status = VoucherStatus.Assigned,
                AssignedToUserId = userId,
                OrderId = orderId,
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            });

            seed.VoucherMovements.Add(new VoucherMovement
            {
                Id = Guid.CreateVersion7(),
                Kind = VoucherMovementKinds.Issued,
                VoucherId = voucherId,
                FromLocation = VoucherLocations.Pool,
                ToLocation = VoucherLocations.Customer,
                OrderId = orderId,
                LineItemId = lineId,
                ActorKind = VoucherActorKinds.User,
                ActorUserId = userId,
                OccurredAtUtc = TestClock.UtcNow,
            });

            await seed.SaveChangesAsync();
        }

        await using (var reportContext = CreateContext())
        {
            var claimed = await reportContext.ExecuteReplaySafeAsync(async ct =>
                await TestRedemptions.For(reportContext).ReportAsync(
                    voucherId,
                    reportedByUserId: userId,
                    actorUserId: userId,
                    reportedLiters: null,
                    redeemedAtUtc: TestClock.UtcNow,
                    supplierReference: null,
                    reason: "seed",
                    ct));

            claimed.Outcome.Should().Be(RedemptionReportOutcome.Reported);
        }

        if (archived)
        {
            await using var archive = CreateContext();
            await archive.FuelVouchers
                .Where(v => v.Id == voucherId)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.IsDeleted, true));
        }
    }

    private async Task SeedUsedVoucherAsync(Guid userId, Guid voucherId)
    {
        await using var seed = CreateContext();
        await ResetDataAsync(seed);

        seed.Users.Add(new User
        {
            Id = userId,
            PhoneNumber = $"+38{userId:N}"[..20],
            IsActive = true,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        seed.FuelVouchers.Add(new FuelVoucher
        {
            Id = voucherId,
            BrandId = BrandIds.ForSlug("okko"),
            Brand = "okko",
            FuelTypeId = ToUuid("okko-95"),
            Liters = 50m,
            SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(1)),
            CustomerExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(1)),
            VoucherNumber = $"OKKO-{voucherId:N}"[..20],
            QrPayload = $"payload-{voucherId:N}",
            Status = VoucherStatus.Used,
            AssignedToUserId = userId,
            OrderId = SeedPurchaseOrder(seed, userId),
            UsedAtUtc = TestClock.UtcNow.AddMinutes(-5),
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        await seed.SaveChangesAsync();
    }

    private static Guid SeedPurchaseOrder(ApplicationDbContext ctx, Guid userId)
    {
        var orderId = Guid.NewGuid();
        ctx.Orders.Add(new Order
        {
            Id = orderId,
            UserId = userId,
            Price = 2500m,
            Status = OrderStatus.Fulfilled,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });
        return orderId;
    }

    private static async Task ResetDataAsync(ApplicationDbContext context)
        => await context.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE
                "journal_lines", "journal_entries", "document_sequences", "accounting_periods",
                "voucher_redemptions", "redemption_statement_rows",
                "voucher_events", "voucher_movements", "order_line_items", "orders",
                "fuel_vouchers", "users", "payments", "fulfillments", "outbox_events",
                "provider_event_outbox", "app_settings"
            RESTART IDENTITY CASCADE
            """);

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);
}

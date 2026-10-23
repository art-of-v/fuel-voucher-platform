using FluentAssertions;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Events;
using FuelFlow.Features.Vouchers.Movements;
using FuelFlow.Features.Vouchers.Redemptions;
using FuelFlow.Features.Vouchers.Redemptions.DecideRedemption;
using FuelFlow.Features.Vouchers.Redemptions.DismissStatementRow;
using FuelFlow.Features.Vouchers.Redemptions.GetExceptions;
using FuelFlow.Features.Vouchers.Redemptions.ImportStatement;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

[Collection("Integration Tests")]
public sealed class RedemptionReconciliationIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public RedemptionReconciliationIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Import_OpensAClaimForAnAssignedVoucher_AndAReimportChangesNothing()
    {
        var seeded = await SeedAssignedVoucherAsync();
        var operatorUserId = Guid.NewGuid();

        var line = new ImportRedemptionStatementLine(
            "supplier-line-1",
            seeded.VoucherNumber,
            50m,
            TestClock.UtcNow.AddHours(-2),
            "pump 7");

        await using (var context = CreateContext())
        {
            var handler = new ImportRedemptionStatementCommandHandler(
                context,
                TestRedemptions.For(context));

            var response = await handler.HandleAsync(
                new ImportRedemptionStatementCommand(
                    operatorUserId,
                    [line, line with { Note = "the same line twice" }]));

            response.Success.Should().BeTrue();
            response.Imported.Should().Be(1);
            response.Duplicates.Should().Be(1);
            response.AwaitingReview.Should().Be(1);
            response.Exceptions.Should().Be(0);
            response.Rows.Should().HaveCount(2);
            response.Rows[0].Status.Should().Be(RedemptionStatementStatus.Matched);
            response.Rows[0].VoucherId.Should().Be(seeded.VoucherId);
            response.Rows[0].RedemptionId.Should().NotBeNull();
            response.Rows[1].Status.Should().BeNull();
        }

        await using (var verify = CreateContext())
        {
            var voucher = await verify.FuelVouchers
                .Where(v => v.Id == seeded.VoucherId)
                .Select(v => v.Status)
                .SingleAsync();
            voucher.Should().Be(VoucherStatus.PendingReconciliation);

            var redemption = await verify.VoucherRedemptions
                .Where(r => r.VoucherId == seeded.VoucherId)
                .SingleAsync();
            redemption.Status.Should().Be(VoucherRedemptionStatus.Reported);
            redemption.ReportedByUserId.Should().BeNull("the statement raised the claim");
            redemption.SupplierReference.Should().Be("supplier-line-1");

            var row = await verify.RedemptionStatementRows.SingleAsync();
            row.Status.Should().Be(RedemptionStatementStatus.Matched);
            row.VoucherRedemptionId.Should().Be(redemption.Id);
            row.ImportedByUserId.Should().Be(operatorUserId);

            (await verify.JournalEntries
                    .Where(e => e.SourceId == seeded.VoucherId)
                    .Select(e => e.Status)
                    .ToListAsync())
                .Should().HaveCount(3)
                .And.OnlyContain(status => status == JournalEntryStatus.Draft);
        }

        await using (var context = CreateContext())
        {
            var handler = new ImportRedemptionStatementCommandHandler(
                context,
                TestRedemptions.For(context));

            var again = await handler.HandleAsync(
                new ImportRedemptionStatementCommand(
                    operatorUserId,
                    [line with { Note = "changed on the supplier's side" }]));

            again.Success.Should().BeTrue();
            again.Imported.Should().Be(0);
            again.Duplicates.Should().Be(1);
            again.Rows.Single().Status.Should().BeNull();
        }

        await using (var verify = CreateContext())
        {
            (await verify.RedemptionStatementRows.CountAsync()).Should().Be(1);
            (await verify.VoucherRedemptions.CountAsync(r => r.VoucherId == seeded.VoucherId))
                .Should().Be(1);
            (await verify.JournalEntries.CountAsync(e => e.SourceId == seeded.VoucherId))
                .Should().Be(3);
        }
    }

    [Fact]
    public async Task A_Mismatch_ReachesTheExceptionQueue_AndTheClaimMustBeDecidedFirst()
    {
        var seeded = await SeedPendingVoucherAsync(reportedLiters: 50m);
        var operatorUserId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            var import = new ImportRedemptionStatementCommandHandler(
                context,
                TestRedemptions.For(context));

            var response = await import.HandleAsync(
                new ImportRedemptionStatementCommand(
                    operatorUserId,
                    [StatementLine(seeded, liters: 42m)]));

            response.Success.Should().BeTrue();
            response.Imported.Should().Be(1);
            response.AwaitingReview.Should().Be(1);
            response.Exceptions.Should().Be(1);
            response.Rows.Single().Status.Should().Be(RedemptionStatementStatus.Mismatch);
        }

        Guid rowId;

        await using (var context = CreateContext())
        {
            var exceptions = await new GetRedemptionExceptionsQueryHandler(context)
                .HandleAsync(new GetRedemptionExceptionsQuery());

            exceptions.PendingClaims.Should().Be(1);
            exceptions.MismatchedClaims.Should().Be(1);
            exceptions.StatementExceptions.Should().Be(0);
            exceptions.Pending.Single().Severity.Should().Be("critical");
            exceptions.Pending.Single().StatementLines.Single().Status
                .Should().Be(RedemptionStatementStatus.Mismatch);
            rowId = exceptions.Pending.Single().StatementLines.Single().RowId;
        }

        await using (var context = CreateContext())
        {
            var dismiss = new DismissRedemptionStatementRowCommandHandler(context);
            var response = await dismiss.HandleAsync(
                new DismissRedemptionStatementRowCommand(rowId, operatorUserId, "checked"));

            response.Success.Should().BeFalse();
            response.ErrorCode.Should().Be("InvalidState");
            response.Message.Should().Contain("confirm or reject the claim");
        }

        await using (var context = CreateContext())
        {
            var decide = new DecideRedemptionCommandHandler(context, TestRedemptions.For(context));
            var response = await decide.HandleAsync(
                new DecideRedemptionCommand(
                    seeded.VoucherId,
                    RedemptionDecisionAction.Reject,
                    operatorUserId,
                    "the pump says 42, the customer said 50"));

            response.Success.Should().BeTrue();
            response.Message.Should().Be("Redemption rejected; the voucher is assignable again");
        }

        await using (var verify = CreateContext())
        {
            var voucher = await verify.FuelVouchers
                .Where(v => v.Id == seeded.VoucherId)
                .Select(v => new { v.Status, v.UsedAtUtc })
                .SingleAsync();
            voucher.Status.Should().Be(VoucherStatus.Assigned);
            voucher.UsedAtUtc.Should().BeNull();

            var redemption = await verify.VoucherRedemptions
                .Where(r => r.VoucherId == seeded.VoucherId)
                .SingleAsync();
            redemption.Status.Should().Be(VoucherRedemptionStatus.Rejected);
            redemption.ClosedByUserId.Should().Be(operatorUserId);

            var row = await verify.RedemptionStatementRows
                .Where(r => r.Id == rowId)
                .SingleAsync();
            row.Status.Should().Be(RedemptionStatementStatus.Rejected);
            row.ResolvedByUserId.Should().Be(operatorUserId);

            (await verify.JournalEntries.CountAsync(e => e.SourceId == seeded.VoucherId))
                .Should().Be(0, "a rejected claim has no sale to post");
        }

        await using (var context = CreateContext())
        {
            var decide = new DecideRedemptionCommandHandler(context, TestRedemptions.For(context));
            var again = await decide.HandleAsync(
                new DecideRedemptionCommand(
                    seeded.VoucherId,
                    RedemptionDecisionAction.Reject,
                    operatorUserId,
                    null));

            again.Success.Should().BeTrue();
            again.Message.Should().Be("Redemption was already rejected");
        }
    }

    [Fact]
    public async Task Confirming_A_MatchedClaim_PostsTheDrafts_AndKeepsTheFuelTime()
    {
        var seeded = await SeedPendingVoucherAsync(reportedLiters: 50m);
        var operatorUserId = Guid.NewGuid();
        Guid redemptionId;

        await using (var context = CreateContext())
        {
            var import = new ImportRedemptionStatementCommandHandler(
                context,
                TestRedemptions.For(context));
            var imported = await import.HandleAsync(
                new ImportRedemptionStatementCommand(
                    operatorUserId,
                    [StatementLine(seeded, liters: 50m)]));

            imported.AwaitingReview.Should().Be(1);
            imported.Exceptions.Should().Be(0);
            redemptionId = imported.Rows.Single().RedemptionId!.Value;
        }

        await using (var context = CreateContext())
        {
            var decide = new DecideRedemptionCommandHandler(context, TestRedemptions.For(context));
            var response = await decide.HandleAsync(
                new DecideRedemptionCommand(
                    seeded.VoucherId,
                    RedemptionDecisionAction.Confirm,
                    operatorUserId,
                    "the statement agrees"));

            response.Success.Should().BeTrue();
            response.Message.Should().Be("Redemption confirmed");
        }

        await using (var verify = CreateContext())
        {
            var voucher = await verify.FuelVouchers
                .Where(v => v.Id == seeded.VoucherId)
                .Select(v => new { v.Status, v.UsedAtUtc })
                .SingleAsync();
            voucher.Status.Should().Be(VoucherStatus.Used);
            voucher.UsedAtUtc.Should().Be(seeded.RedeemedAtUtc);

            var redemption = await verify.VoucherRedemptions
                .Where(r => r.Id == redemptionId)
                .SingleAsync();
            redemption.Status.Should().Be(VoucherRedemptionStatus.Confirmed);
            redemption.ConfirmedByUserId.Should().Be(operatorUserId);
            redemption.ConfirmedAtUtc.Should().BeCloseTo(TestClock.UtcNow, TimeSpan.FromSeconds(10));

            var row = await verify.RedemptionStatementRows.SingleAsync();
            row.Status.Should().Be(RedemptionStatementStatus.Confirmed);
            row.ResolvedByUserId.Should().Be(operatorUserId);
            row.ResolvedAtUtc.Should().NotBeNull();

            (await verify.JournalEntries
                    .Where(e => e.SourceId == seeded.VoucherId)
                    .Select(e => e.Status)
                    .ToListAsync())
                .Should().HaveCount(3)
                .And.OnlyContain(status => status == JournalEntryStatus.Posted);

            var exceptions = await new GetRedemptionExceptionsQueryHandler(verify)
                .HandleAsync(new GetRedemptionExceptionsQuery());
            exceptions.PendingClaims.Should().Be(0);
            exceptions.StatementExceptions.Should().Be(0);
        }

        await using (var context = CreateContext())
        {
            var decide = new DecideRedemptionCommandHandler(context, TestRedemptions.For(context));
            var again = await decide.HandleAsync(
                new DecideRedemptionCommand(
                    seeded.VoucherId,
                    RedemptionDecisionAction.Confirm,
                    operatorUserId,
                    null));

            again.Success.Should().BeTrue();
            again.Message.Should().Be("Redemption was already confirmed");
        }

        await using (var verify = CreateContext())
        {
            (await verify.JournalEntries.CountAsync(e => e.SourceId == seeded.VoucherId))
                .Should().Be(3, "a repeated decision posts nothing new");
        }
    }

    [Fact]
    public async Task Reversing_A_ConfirmedClaim_ReturnsTheVoucher_AndReversesTheJournal()
    {
        var seeded = await SeedAssignedVoucherAsync();
        var operatorUserId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            var import = new ImportRedemptionStatementCommandHandler(
                context,
                TestRedemptions.For(context));
            await import.HandleAsync(
                new ImportRedemptionStatementCommand(
                    operatorUserId,
                    [StatementLine(seeded, liters: 50m)]));
        }

        await using (var context = CreateContext())
        {
            var decide = new DecideRedemptionCommandHandler(context, TestRedemptions.For(context));
            var confirmed = await decide.HandleAsync(
                new DecideRedemptionCommand(
                    seeded.VoucherId,
                    RedemptionDecisionAction.Confirm,
                    operatorUserId,
                    null));
            confirmed.Success.Should().BeTrue();
        }

        await using (var context = CreateContext())
        {
            var decide = new DecideRedemptionCommandHandler(context, TestRedemptions.For(context));
            var response = await decide.HandleAsync(
                new DecideRedemptionCommand(
                    seeded.VoucherId,
                    RedemptionDecisionAction.Reverse,
                    operatorUserId,
                    "the supplier withdrew the charge"));

            response.Success.Should().BeTrue();
            response.Message.Should().Be("Redemption reversed");
        }

        await using (var verify = CreateContext())
        {
            var voucher = await verify.FuelVouchers
                .Where(v => v.Id == seeded.VoucherId)
                .Select(v => new { v.Status, v.UsedAtUtc })
                .SingleAsync();
            voucher.Status.Should().Be(VoucherStatus.Assigned);
            voucher.UsedAtUtc.Should().BeNull();

            var redemption = await verify.VoucherRedemptions
                .Where(r => r.VoucherId == seeded.VoucherId)
                .SingleAsync();
            redemption.Status.Should().Be(VoucherRedemptionStatus.Reversed);
            redemption.ClosedByUserId.Should().Be(operatorUserId);

            var row = await verify.RedemptionStatementRows.SingleAsync();
            row.Status.Should().Be(RedemptionStatementStatus.Reversed);

            var originals = await verify.JournalEntries
                .Where(e => e.SourceId == seeded.VoucherId)
                .ToListAsync();
            originals.Should().HaveCount(3);
            originals.Should().OnlyContain(e => e.Status == JournalEntryStatus.Reversed);
            originals.Should().OnlyContain(e => e.ReversedByEntryId != null);

            var reversalIds = originals.Select(e => e.ReversedByEntryId!.Value).ToArray();
            var reversals = await verify.JournalEntries
                .Where(e => reversalIds.Contains(e.Id))
                .ToListAsync();
            reversals.Should().HaveCount(3);
            reversals.Should().OnlyContain(e => e.Status == JournalEntryStatus.Posted);
            reversals.Should().OnlyContain(e => e.SourceType == JournalSourceType.Reversal);
        }
    }

    [Fact]
    public async Task An_UnknownLine_IsAnException_ThatAnOperatorMayDismiss()
    {
        await ResetAsync();
        var operatorUserId = Guid.NewGuid();
        Guid rowId;

        await using (var context = CreateContext())
        {
            var import = new ImportRedemptionStatementCommandHandler(
                context,
                TestRedemptions.For(context));
            var response = await import.HandleAsync(
                new ImportRedemptionStatementCommand(
                    operatorUserId,
                    [new ImportRedemptionStatementLine(
                        "unknown-1",
                        "NO-SUCH-VOUCHER",
                        25m,
                        null,
                        "typo on the pump report")]));

            response.Success.Should().BeTrue();
            response.Imported.Should().Be(1);
            response.AwaitingReview.Should().Be(1);
            response.Exceptions.Should().Be(1);
            response.Rows.Single().Status.Should().Be(RedemptionStatementStatus.UnknownVoucher);
        }

        await using (var context = CreateContext())
        {
            var exceptions = await new GetRedemptionExceptionsQueryHandler(context)
                .HandleAsync(new GetRedemptionExceptionsQuery());

            exceptions.PendingClaims.Should().Be(0);
            exceptions.StatementExceptions.Should().Be(1);
            exceptions.Statements.Single().Severity.Should().Be("critical");
            exceptions.Statements.Single().VoucherId.Should().BeNull();
            rowId = exceptions.Statements.Single().RowId;
        }

        await using (var context = CreateContext())
        {
            var dismiss = new DismissRedemptionStatementRowCommandHandler(context);
            var response = await dismiss.HandleAsync(
                new DismissRedemptionStatementRowCommand(rowId, operatorUserId, "the number was mistyped"));

            response.Success.Should().BeTrue();
            response.Message.Should().Be("Statement line dismissed");
        }

        await using (var verify = CreateContext())
        {
            var row = await verify.RedemptionStatementRows.SingleAsync();
            row.Status.Should().Be(RedemptionStatementStatus.Dismissed);
            row.ResolvedByUserId.Should().Be(operatorUserId);
            row.ResolutionNote.Should().Be("the number was mistyped");

            var exceptions = await new GetRedemptionExceptionsQueryHandler(verify)
                .HandleAsync(new GetRedemptionExceptionsQuery());
            exceptions.StatementExceptions.Should().Be(0);
        }

        await using (var context = CreateContext())
        {
            var dismiss = new DismissRedemptionStatementRowCommandHandler(context);
            var again = await dismiss.HandleAsync(
                new DismissRedemptionStatementRowCommand(rowId, operatorUserId, null));

            again.Success.Should().BeTrue();
            again.Message.Should().Be("Statement line was already dismissed");
        }
    }

    [Fact]
    public async Task An_AlreadyConfirmedLine_IsAnException_ThatDoesNotBlockTheImport()
    {
        var seeded = await SeedConfirmedVoucherAsync();
        var operatorUserId = Guid.NewGuid();
        Guid rowId;

        await using (var context = CreateContext())
        {
            var import = new ImportRedemptionStatementCommandHandler(
                context,
                TestRedemptions.For(context));
            var response = await import.HandleAsync(
                new ImportRedemptionStatementCommand(
                    operatorUserId,
                    [StatementLine(seeded, liters: 50m)]));

            response.Success.Should().BeTrue();
            response.Imported.Should().Be(1);
            response.AwaitingReview.Should().Be(1);
            response.Exceptions.Should().Be(1);
            response.Rows.Single().Status.Should().Be(RedemptionStatementStatus.AlreadyConfirmed);
            response.Rows.Single().RedemptionId.Should().NotBeNull();
        }

        await using (var context = CreateContext())
        {
            var exceptions = await new GetRedemptionExceptionsQueryHandler(context)
                .HandleAsync(new GetRedemptionExceptionsQuery());

            exceptions.PendingClaims.Should().Be(0);
            exceptions.StatementExceptions.Should().Be(1);
            exceptions.Statements.Single().Severity.Should().Be("warning");
            rowId = exceptions.Statements.Single().RowId;
        }

        await using (var context = CreateContext())
        {
            var dismiss = new DismissRedemptionStatementRowCommandHandler(context);
            var response = await dismiss.HandleAsync(
                new DismissRedemptionStatementRowCommand(rowId, operatorUserId, "already accounted for"));

            response.Success.Should().BeTrue();
        }

        await using (var verify = CreateContext())
        {
            var row = await verify.RedemptionStatementRows.SingleAsync();
            row.Status.Should().Be(RedemptionStatementStatus.Dismissed);

            (await verify.JournalEntries.CountAsync(e => e.SourceId == seeded.VoucherId))
                .Should().Be(3, "the import did not post a second sale");
        }
    }

    private static ImportRedemptionStatementLine StatementLine(SeededVoucher seeded, decimal liters) =>
        new(
            $"supplier-{Guid.NewGuid():N}",
            seeded.VoucherNumber,
            liters,
            seeded.RedeemedAtUtc,
            "pump report");

    private async Task<SeededVoucher> SeedAssignedVoucherAsync()
    {
        await ResetAsync();

        var holderId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();
        var voucherNumber = $"OKKO-{voucherId:N}"[..20];
        var redeemedAt = TestClock.UtcNow.AddHours(-2);

        await using var seed = CreateContext();

        seed.Users.Add(new User
        {
            Id = holderId,
            PhoneNumber = $"+38{holderId:N}"[..20],
            IsActive = true,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow,
        });

        var orderId = SeedPurchaseOrder(seed, holderId);
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
            VoucherNumber = voucherNumber,
            QrPayload = $"payload-{voucherId:N}",
            Status = VoucherStatus.Assigned,
            AssignedToUserId = holderId,
            OrderId = orderId,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow,
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
            ActorUserId = holderId,
            OccurredAtUtc = TestClock.UtcNow,
        });

        await seed.SaveChangesAsync();

        return new SeededVoucher(holderId, voucherId, voucherNumber, redeemedAt);
    }

    private async Task<SeededVoucher> SeedPendingVoucherAsync(decimal reportedLiters)
    {
        var seeded = await SeedAssignedVoucherAsync();

        await using var context = CreateContext();
        var claimed = await context.ExecuteReplaySafeAsync(async ct =>
            await TestRedemptions.For(context).ReportAsync(
                seeded.VoucherId,
                reportedByUserId: seeded.HolderId,
                actorUserId: seeded.HolderId,
                reportedLiters: reportedLiters,
                redeemedAtUtc: seeded.RedeemedAtUtc,
                supplierReference: null,
                reason: "self-reported",
                ct));

        claimed.Outcome.Should().Be(RedemptionReportOutcome.Reported);

        return seeded;
    }

    private async Task<SeededVoucher> SeedConfirmedVoucherAsync()
    {
        var seeded = await SeedPendingVoucherAsync(50m);
        var operatorUserId = Guid.NewGuid();

        await using var context = CreateContext();
        var confirmed = await context.ExecuteReplaySafeAsync(async ct =>
            await TestRedemptions.For(context).ConfirmAsync(
                seeded.VoucherId,
                operatorUserId,
                "evidence checked",
                ct));

        confirmed.Outcome.Should().Be(RedemptionDecisionOutcome.Confirmed);

        return seeded;
    }

    private static Guid SeedPurchaseOrder(ApplicationDbContext context, Guid userId)
    {
        var orderId = Guid.NewGuid();

        context.Orders.Add(new Order
        {
            Id = orderId,
            UserId = userId,
            Price = 2500m,
            Status = OrderStatus.Fulfilled,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow,
        });

        return orderId;
    }

    private async Task ResetAsync()
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE
                "journal_lines", "journal_entries", "document_sequences", "accounting_periods",
                "voucher_redemptions", "redemption_statement_rows",
                "voucher_events", "voucher_movements", "order_line_items", "orders",
                "fuel_vouchers", "users", "payments", "fulfillments", "outbox_events",
                "provider_event_outbox", "app_settings"
            RESTART IDENTITY CASCADE
            """);
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    private sealed record SeededVoucher(
        Guid HolderId,
        Guid VoucherId,
        string VoucherNumber,
        DateTime RedeemedAtUtc);
}

using FluentAssertions;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Renewal.Checkout;
using FuelFlow.Features.Vouchers.Renewal.Operator;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Features.Vouchers.Events;
using FuelFlow.Features.Vouchers.Movements;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Testcontainers coverage for the operator-initiated renewal of a CUSTOMER's voucher (follow-up to
/// #104). Drives <see cref="ConfirmOperatorRenewalCommandHandler"/> against real Postgres so the
/// tracked two-branch mutations, the status flips and the audit row are exercised end-to-end: extend
/// in place, replace from stock, the no-stock 409 (nothing mutated), the validation rejections, and a
/// zero surcharge stored verbatim.
/// </summary>
[Collection("Integration Tests")]
public sealed class OperatorVoucherRenewalIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public OperatorVoucherRenewalIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Extend_StillValidCustomerVoucher_PushesExpiryAndRecordsRow()
    {
        var userId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();
        var actingUserId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var oldExpiry = today.AddDays(5); // still valid → Extend

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);
            SeedUser(seed, userId);
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);
            seed.FuelVouchers.Add(CustomerVoucher(voucherId, userId, purchaseOrderId, "OKKO", "okko-95", 50m, oldExpiry, supplierExpiry: today.AddMonths(12)));
            await seed.SaveChangesAsync();
        }

        ConfirmOperatorRenewalResult result;
        using (var ctx = CreateContext())
        {
            result = await Handler(ctx).HandleAsync(new ConfirmOperatorRenewalCommand
            {
                VoucherId = voucherId,
                TermCode = "1m",
                SurchargeUah = 120.50m,
                InvoiceNumber = "INV-1",
                InvoiceDate = today,
                ActingUserId = actingUserId,
                ActingUserName = "Op Erator"
            });
        }

        result.Branch.Should().Be("extend");
        result.ReplacementVoucherId.Should().BeNull();
        result.OldExpiration.Should().Be(oldExpiry);
        result.NewExpiration.Should().Be(oldExpiry.AddMonths(1));

        using var verify = CreateContext();

        var voucher = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == voucherId);
        voucher.Status.Should().Be(VoucherStatus.Assigned); // same row, still the customer's
        voucher.AssignedToUserId.Should().Be(userId);
        voucher.CustomerExpirationDate.Should().Be(oldExpiry.AddMonths(1)); // OLD expiry + term, leftover days kept

        var row = await verify.OperatorVoucherRenewals.AsNoTracking().FirstAsync(r => r.VoucherId == voucherId);
        row.Branch.Should().Be("extend");
        row.ReplacementVoucherId.Should().BeNull();
        row.TermCode.Should().Be("1m");
        row.SurchargeUah.Should().Be(120.50m);
        row.CustomerUserId.Should().Be(userId);
        row.ActingUserId.Should().Be(actingUserId);
        row.InvoiceNumber.Should().Be("INV-1");

        var audit = await verify.AuditEventOutbox.AsNoTracking()
            .Where(e => e.AggregateId == voucherId.ToString() && e.EventType == "VoucherRenewedByOperator")
            .ToListAsync();
        audit.Should().ContainSingle();

        // D-13. The operator path now records the same `Extended` event the self-serve checkout does,
        // with the pair - the old expiry is overwritten in place two lines above, so without this the
        // value before the renewal is gone.
        //
        // And no StatusChanged: `IsRenewableStatus` admits an already-`Assigned` voucher, so status may
        // be identical before and after, and ck_voucher_events_status_changed would refuse from == to.
        // An extend changes the term, not the state - saying otherwise would be an invention.
        var events = await verify.VoucherEvents.AsNoTracking()
            .Where(e => e.VoucherId == voucherId)
            .ToListAsync();
        var extended = events.Should().ContainSingle().Subject;
        extended.EventType.Should().Be(VoucherEventTypes.Extended);
        extended.PreviousCustomerExpiration.Should().Be(oldExpiry);
        extended.NewCustomerExpiration.Should().Be(oldExpiry.AddMonths(1));
        extended.FromStatus.Should().BeNull("an extend is not a status change");
        extended.ToStatus.Should().BeNull();
        extended.ActorKind.Should().Be(VoucherActorKinds.User);
        extended.ActorUserId.Should().Be(actingUserId, "an operator pressed this, not a scheduler");
    }

    /// <summary>
    /// The ceiling, on the operator path. The voucher's paper has five days left and the operator asked
    /// for a month, so extending would promise fuel the pump stops honouring on day six.
    ///
    /// <para>
    /// Refused rather than quietly downgraded to a replace: the operator chose this term, and silently
    /// performing a different operation is the failure mode this codebase argues against elsewhere.
    /// The same refusal, with the same code, is what the self-serve checkout does at
    /// <c>RenewalCheckoutCommandHandler.cs:135</c>, and the database now enforces the rule underneath
    /// both in <c>ck_voucher_customer_term_within_supplier_term</c>.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Extend_BeyondTheSupplierTerm_IsRefused()
    {
        var userId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();
        var actingUserId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var customerExpiry = today.AddDays(5);
        // Deliberately NOT given a supplierExpiry override: both dates are the same, five days out.

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);
            SeedUser(seed, userId);
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);
            seed.FuelVouchers.Add(CustomerVoucher(voucherId, userId, purchaseOrderId, "OKKO", "okko-95", 50m, customerExpiry));
            await seed.SaveChangesAsync();
        }

        var act = async () =>
        {
            await using var ctx = CreateContext();
            return await Handler(ctx).HandleAsync(new ConfirmOperatorRenewalCommand
            {
                VoucherId = voucherId,
                TermCode = "1m",
                SurchargeUah = 120.50m,
                ActingUserId = actingUserId,
                ActingUserName = "Op Erator"
            });
        };

        var thrown = await act.Should().ThrowAsync<VoucherRenewalException>();
        thrown.Which.Code.Should().Be("provider_term_exhausted");

        using var verify = CreateContext();
        var voucher = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == voucherId);
        voucher.CustomerExpirationDate.Should().Be(customerExpiry,
            "a refused renewal must leave the customer exactly where they were");
        voucher.Status.Should().Be(VoucherStatus.Assigned);
        (await verify.OperatorVoucherRenewals.AsNoTracking().AnyAsync(r => r.VoucherId == voucherId))
            .Should().BeFalse("no settlement row for an operation that did not happen");
    }

    [Fact]
    public async Task Replace_LapsedCustomerVoucher_AssignsStockAndReturnsOldToThePool()
    {
        var userId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var originPurchaseId = Guid.NewGuid();
        // Named so the D-13 assertions below can say WHO was recorded - an inline Guid.NewGuid()
        // would make the actor unassertable.
        var actingUserId = Guid.NewGuid();

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);
            SeedUser(seed, userId);
            originPurchaseId = SeedPurchaseOrder(seed, userId);
            // Lapsed source → Replace branch; seeded Assigned so we can prove it goes back to stock.
            seed.FuelVouchers.Add(CustomerVoucher(voucherId, userId, originPurchaseId, "OKKO", "okko-95", 40m, today.AddDays(-3)));
            // Matching stock, valid well beyond today + 1 month.
            seed.FuelVouchers.Add(StockVoucher(stockId, "OKKO", "okko-95", 40m, today.AddMonths(6)));
            await seed.SaveChangesAsync();
        }

        ConfirmOperatorRenewalResult result;
        using (var ctx = CreateContext())
        {
            result = await Handler(ctx).HandleAsync(new ConfirmOperatorRenewalCommand
            {
                VoucherId = voucherId,
                TermCode = "1m",
                SurchargeUah = 300m,
                ActingUserId = actingUserId
            });
        }

        result.Branch.Should().Be("replace");
        result.ReplacementVoucherId.Should().Be(stockId);
        // The source already lapsed, so it keeps no leftover days and the promise is simply the month
        // that was paid for — NOT the stock voucher's full paper term.
        result.NewExpiration.Should().Be(today.AddMonths(1));

        using var verify = CreateContext();

        var stock = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == stockId);
        stock.Status.Should().Be(VoucherStatus.Assigned);
        stock.AssignedToUserId.Should().Be(userId);
        stock.LegalEntityId.Should().BeNull(); // inherited from the source (which had none)
        stock.WorkerUserId.Should().BeNull();
        stock.CustomerExpirationDate.Should().Be(today.AddMonths(1),
            "the customer is handed the term they paid for; the rest of the paper term is a hidden reserve");

        var source = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == voucherId);
        source.Status.Should().Be(VoucherStatus.Available); // back in the sellable pool
        source.AssignedToUserId.Should().BeNull();
        source.OrderId.Should().Be(originPurchaseId, "it keeps the purchase it arrived under — not re-homed");

        var row = await verify.OperatorVoucherRenewals.AsNoTracking().FirstAsync(r => r.VoucherId == voucherId);
        row.Branch.Should().Be("replace");
        row.ReplacementVoucherId.Should().Be(stockId);
        row.NewExpiration.Should().Be(today.AddMonths(1));

        // D-13. The same pair the self-serve renewal writes, for the same reason: a replace makes two
        // statements, so one row would have recorded only one of them. `replace_out` for the replacement
        // leaving the pool, `returned` for the customer's own paper coming back.
        var movements = await verify.VoucherMovements.AsNoTracking()
            .Where(m => m.VoucherId == stockId || m.VoucherId == voucherId)
            .ToListAsync();
        movements.Should().HaveCount(2, "a replace moves the replacement out and the old paper back");

        var out_ = movements.Should().ContainSingle(m => m.Kind == VoucherMovementKinds.ReplaceOut).Subject;
        out_.VoucherId.Should().Be(stockId, "the replacement is what left the pool");
        out_.FromLocation.Should().Be(VoucherLocations.Pool);
        out_.ToLocation.Should().Be(VoucherLocations.Customer);

        var back = movements.Should().ContainSingle(m => m.Kind == VoucherMovementKinds.Returned).Subject;
        back.VoucherId.Should().Be(voucherId, "the old paper is what came back");
        back.FromLocation.Should().Be(VoucherLocations.Customer);
        back.ToLocation.Should().Be(VoucherLocations.Pool);
        movements.Should().OnlyContain(m => m.ActorUserId == actingUserId);

        // Both transitions are recorded, and the source's from_status is the value it actually had -
        // a renewable voucher may be Assigned or Expired, and the history has to say which.
        var events = await verify.VoucherEvents.AsNoTracking()
            .Where(e => e.VoucherId == stockId || e.VoucherId == voucherId)
            .ToListAsync();
        events.Should().HaveCount(2);

        var claim = events.Should().ContainSingle(e => e.VoucherId == stockId).Subject;
        claim.EventType.Should().Be(VoucherEventTypes.StatusChanged);
        claim.FromStatus.Should().Be(nameof(VoucherStatus.Available));
        claim.ToStatus.Should().Be(nameof(VoucherStatus.Assigned));

        var release = events.Should().ContainSingle(e => e.VoucherId == voucherId).Subject;
        release.EventType.Should().Be(VoucherEventTypes.StatusChanged);
        release.ToStatus.Should().Be(nameof(VoucherStatus.Available));
        release.FromStatus.Should().NotBe(release.ToStatus,
            "ck_voucher_events_status_changed refuses from == to, so a genuine release never records one");
    }

    [Fact]
    public async Task Replace_NoStock_Throws409AndMutatesNothing()
    {
        var userId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);
            SeedUser(seed, userId);
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);
            seed.FuelVouchers.Add(CustomerVoucher(voucherId, userId, purchaseOrderId, "WOG", "wog-95", 40m, today.AddDays(-3)));
            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            var act = async () => await Handler(ctx).HandleAsync(new ConfirmOperatorRenewalCommand
            {
                VoucherId = voucherId,
                TermCode = "1m",
                SurchargeUah = 0m,
                ActingUserId = Guid.NewGuid()
            });

            (await act.Should().ThrowAsync<VoucherRenewalException>()).Which.Code.Should().Be("no_stock");
        }

        using var verify = CreateContext();
        var voucher = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == voucherId);
        voucher.Status.Should().Be(VoucherStatus.Assigned); // untouched — transaction rolled back
        (await verify.OperatorVoucherRenewals.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    /// <summary>
    /// A replace hands over stock we paid for, so the surcharge the operator types is a sale price. It
    /// used to be compared against nothing at all: a surcharge under the stock's cost lost money on
    /// every renewal, silently and on repeat.
    /// </summary>
    [Fact]
    public async Task Replace_SurchargeBelowStockCost_IsRefusedAndMutatesNothing()
    {
        var userId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);
            SeedUser(seed, userId);
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);
            seed.FuelVouchers.Add(CustomerVoucher(voucherId, userId, purchaseOrderId, "OKKO", "okko-95", 40m, today.AddDays(-3)));
            // Stock that cost us 50/litre: 40 L is 2000 UAH, so a 300 surcharge is far under it.
            seed.FuelVouchers.Add(StockVoucher(stockId, "OKKO", "okko-95", 40m, today.AddMonths(6), costPerLiter: 50m));
            // Set explicitly: fuel_types is not truncated between tests, so the opt-in test that runs
            // before or after this one must not decide the outcome here.
            await seed.FuelTypes
                .Where(f => f.Id == "okko-95")
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.AllowBelowCost, false));
            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            var act = async () => await Handler(ctx).HandleAsync(new ConfirmOperatorRenewalCommand
            {
                VoucherId = voucherId,
                TermCode = "1m",
                SurchargeUah = 300m,
                ActingUserId = Guid.NewGuid()
            });

            var ex = (await act.Should().ThrowAsync<VoucherRenewalException>()).Which;
            ex.Code.Should().Be("below_cost");
            ex.Message.Should().Contain("1700"); // 2000 cost − 300 collected
            // The figure also has to travel as data: the admin localises this refusal, and a
            // translated sentence with the shortfall baked into the English prose is a sentence the
            // operator can read but not act on. Without this the operator loses the only number.
            ex.Data.Should().ContainKey("shortfallUah");
            ex.Data["shortfallUah"].Should().Be(1700m);
        }

        using var verify = CreateContext();
        (await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == stockId))
            .Status.Should().Be(VoucherStatus.Available, "the transaction rolled back");
        (await verify.OperatorVoucherRenewals.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Replace_SurchargeAboveStockCost_IsAccepted()
    {
        var userId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);
            SeedUser(seed, userId);
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);
            seed.FuelVouchers.Add(CustomerVoucher(voucherId, userId, purchaseOrderId, "OKKO", "okko-95", 40m, today.AddDays(-3)));
            seed.FuelVouchers.Add(StockVoucher(stockId, "OKKO", "okko-95", 40m, today.AddMonths(6), costPerLiter: 50m));
            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            var result = await Handler(ctx).HandleAsync(new ConfirmOperatorRenewalCommand
            {
                VoucherId = voucherId,
                TermCode = "1m",
                SurchargeUah = 2500m, // over the 2000 cost
                ActingUserId = Guid.NewGuid()
            });

            result.Branch.Should().Be("replace");
            result.ReplacementVoucherId.Should().Be(stockId);
        }
    }

    [Fact]
    public async Task Replace_BelowCostSurcharge_IsAllowedWhenTheFuelIsOptedIn()
    {
        var userId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);
            SeedUser(seed, userId);
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);
            seed.FuelVouchers.Add(CustomerVoucher(voucherId, userId, purchaseOrderId, "OKKO", "okko-95", 40m, today.AddDays(-3)));
            seed.FuelVouchers.Add(StockVoucher(stockId, "OKKO", "okko-95", 40m, today.AddMonths(6), costPerLiter: 50m));
            await seed.FuelTypes
                .Where(f => f.Id == "okko-95")
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.AllowBelowCost, true));
            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            // The same standing opt-in the fuel panel offers: a deliberate release of near-expiry
            // stock, entered by a person, must not be second-guessed here.
            var result = await Handler(ctx).HandleAsync(new ConfirmOperatorRenewalCommand
            {
                VoucherId = voucherId,
                TermCode = "1m",
                SurchargeUah = 300m,
                ActingUserId = Guid.NewGuid()
            });

            result.Branch.Should().Be("replace");
        }
    }

    [Fact]
    public async Task Confirm_ZeroSurcharge_IsAcceptedAndStored()
    {
        var userId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);
            SeedUser(seed, userId);
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);
            seed.FuelVouchers.Add(CustomerVoucher(voucherId, userId, purchaseOrderId, "OKKO", "okko-95", 20m, today.AddDays(10), supplierExpiry: today.AddMonths(12)));
            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            await Handler(ctx).HandleAsync(new ConfirmOperatorRenewalCommand
            {
                VoucherId = voucherId,
                TermCode = "2w",
                SurchargeUah = 0m,
                ActingUserId = Guid.NewGuid()
            });
        }

        using var verify = CreateContext();
        var row = await verify.OperatorVoucherRenewals.AsNoTracking().FirstAsync(r => r.VoucherId == voucherId);
        row.SurchargeUah.Should().Be(0m);
    }

    [Fact]
    public async Task Confirm_UnassignedStockVoucher_RejectedAsNotCustomerVoucher()
    {
        var stockId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);
            seed.FuelVouchers.Add(StockVoucher(stockId, "OKKO", "okko-95", 50m, today.AddMonths(3)));
            await seed.SaveChangesAsync();
        }

        using var ctx = CreateContext();
        var act = async () => await Handler(ctx).HandleAsync(new ConfirmOperatorRenewalCommand
        {
            VoucherId = stockId,
            TermCode = "1m",
            SurchargeUah = 0m,
            ActingUserId = Guid.NewGuid()
        });

        (await act.Should().ThrowAsync<VoucherRenewalException>()).Which.Code.Should().Be("not_customer_voucher");
    }

    [Fact]
    public async Task Confirm_UsedVoucher_RejectedAsNotRenewable()
    {
        var userId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);
            SeedUser(seed, userId);
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);
            var v = CustomerVoucher(voucherId, userId, purchaseOrderId, "OKKO", "okko-95", 50m, today.AddDays(5));
            v.Status = VoucherStatus.Used;
            seed.FuelVouchers.Add(v);
            await seed.SaveChangesAsync();
        }

        using var ctx = CreateContext();
        var act = async () => await Handler(ctx).HandleAsync(new ConfirmOperatorRenewalCommand
        {
            VoucherId = voucherId,
            TermCode = "1m",
            SurchargeUah = 0m,
            ActingUserId = Guid.NewGuid()
        });

        (await act.Should().ThrowAsync<VoucherRenewalException>()).Which.Code.Should().Be("not_renewable");
    }

    [Fact]
    public async Task Confirm_UnknownTerm_Rejected()
    {
        var userId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);
            SeedUser(seed, userId);
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);
            seed.FuelVouchers.Add(CustomerVoucher(voucherId, userId, purchaseOrderId, "OKKO", "okko-95", 50m, today.AddDays(5), supplierExpiry: today.AddMonths(12)));
            await seed.SaveChangesAsync();
        }

        using var ctx = CreateContext();
        var act = async () => await Handler(ctx).HandleAsync(new ConfirmOperatorRenewalCommand
        {
            VoucherId = voucherId,
            TermCode = "99y",
            SurchargeUah = 0m,
            ActingUserId = Guid.NewGuid()
        });

        (await act.Should().ThrowAsync<VoucherRenewalException>()).Which.Code.Should().Be("unknown_term");
    }

    [Fact]
    public async Task Confirm_UnknownVoucher_RejectedAsNotFound()
    {
        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);
        }

        using var ctx = CreateContext();
        var act = async () => await Handler(ctx).HandleAsync(new ConfirmOperatorRenewalCommand
        {
            VoucherId = Guid.NewGuid(),
            TermCode = "1m",
            SurchargeUah = 0m,
            ActingUserId = Guid.NewGuid()
        });

        (await act.Should().ThrowAsync<VoucherRenewalException>()).Which.Code.Should().Be("not_found");
    }

    [Fact]
    public async Task Confirm_BlocksOnTheAdvisoryLock_BeforeItReadsTheVoucher()
    {
        // Serializing two operators on one voucher is the whole point, and the lock has to be taken
        // BEFORE the load - a lock after the read would queue the writers while both still extend
        // from the expiration they had already captured.
        //
        // This asserts that ordering deterministically instead of by racing: hold the same lock from
        // an outside connection, and the handler must not get past it. Firing N parallel callers and
        // counting rows would NOT prove it - the thread pool happily runs them one after another,
        // and then each is a legitimate sequential extension of the same voucher (the Extend branch
        // keeps the same voucher, so extending it several times over its life is correct).
        var userId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();
        var actingUserId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);
            SeedUser(seed, userId);
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);
            seed.FuelVouchers.Add(CustomerVoucher(voucherId, userId, purchaseOrderId, "OKKO", "okko-95", 50m, today.AddDays(5), supplierExpiry: today.AddMonths(12)));
            await seed.SaveChangesAsync();
        }

        var handlerTask = Task.Run(async () =>
        {
            await using var ctx = CreateContext();
            return await Handler(ctx).HandleAsync(new ConfirmOperatorRenewalCommand
            {
                VoucherId = voucherId,
                TermCode = "1m",
                SurchargeUah = 120.50m,
                ActingUserId = actingUserId
            });
        });

        // Grab the very lock the handler is about to want, from a separate connection.
        await using var blocker = CreateContext();
        await using var blockerTx = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext('operator-renewal-voucher'), hashtext({voucherId.ToString()}))");

        // The handler is now either blocked on the lock or has not reached it yet. Either way it must
        // not have finished: nobody can complete this renewal while another session holds the lock.
        var completedEarly = await Task.WhenAny(handlerTask, Task.Delay(TimeSpan.FromSeconds(3))) == handlerTask;
        completedEarly.Should().BeFalse(
            "the handler completed while another session held the advisory lock on this voucher - " +
            "the lock is not being taken before the work");

        await blockerTx.RollbackAsync();

        (await handlerTask).Should().NotBeNull("releasing the lock lets the renewal through");

        using var verify = CreateContext();
        (await verify.OperatorVoucherRenewals.CountAsync(r => r.VoucherId == voucherId)).Should().Be(1);
        (await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == voucherId))
            .CustomerExpirationDate.Should().Be(today.AddDays(5).AddMonths(1));
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private static ConfirmOperatorRenewalCommandHandler Handler(ApplicationDbContext ctx)
        => new(ctx, new AuditEventService(ctx));

    private static void SeedUser(ApplicationDbContext ctx, Guid userId)
        => ctx.Users.Add(new User
        {
            Id = userId,
            PhoneNumber = $"+38{userId:N}"[..20],
            FirstName = "Cust",
            LastName = "Omer",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

    /// <summary>
    /// The purchase that originally delivered the customer's voucher. A held voucher (Assigned/Used/
    /// Blocked) must carry an <c>order_id</c> — ck_voucher_held_has_order rejects the row otherwise.
    /// It is deliberately NOT the renewal order: the handler creates that one per confirmation and
    /// points the replacement stock at it itself, while an extended voucher keeps the purchase.
    /// </summary>
    private static Guid SeedPurchaseOrder(ApplicationDbContext ctx, Guid userId)
    {
        var orderId = Guid.NewGuid();
        ctx.Orders.Add(new Order
        {
            Id = orderId,
            UserId = userId,
            Price = 0,
            Status = OrderStatus.Fulfilled,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        return orderId;
    }

/// <param name="expiry">Both dates, unless <paramref name="supplierExpiry"/> says otherwise.</param>
/// <param name="supplierExpiry">
/// The supplier's own term, defaulted to the same date. Pass a later one for an Extend case: the
/// ceiling rule (ck_voucher_customer_term_within_supplier_term) means a voucher whose paper runs out
/// in five days cannot be extended by a month at all - that is a replace, and the handler refuses it.
/// </param>
    private static FuelVoucher CustomerVoucher(
        Guid id, Guid userId, Guid orderId, string brand, string fuelTypeId, decimal liters,
        DateOnly expiry, DateOnly? supplierExpiry = null)
        => new()
    {
        Id = id,
        Brand = brand,
        FuelTypeId = fuelTypeId,
        Liters = liters,
        SupplierExpirationDate = supplierExpiry ?? expiry,
        CustomerExpirationDate = expiry,
            VoucherNumber = $"CUS-{id:N}"[..16],
            QrPayload = $"qr-{id:N}",
            Status = VoucherStatus.Assigned,
            AssignedToUserId = userId,
            OrderId = orderId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    private static FuelVoucher StockVoucher(Guid id, string brand, string fuelTypeId, decimal liters, DateOnly expiry, decimal? costPerLiter = null)
        => new()
        {
            Id = id,
            Brand = brand,
            FuelTypeId = fuelTypeId,
            Liters = liters,
            SupplierExpirationDate = expiry,
            CustomerExpirationDate = expiry,
            VoucherNumber = $"STK-{id:N}"[..16],
            QrPayload = $"qr-{id:N}",
            Status = VoucherStatus.Available,
            CostPerLiter = costPerLiter,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    private static async Task ResetDataAsync(ApplicationDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "operator_voucher_renewals", "fuel_vouchers", "users", "provider_event_outbox" RESTART IDENTITY CASCADE""");
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);
}

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
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

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
        var today = DateOnly.FromDateTime(TestClock.UtcNow);
        var oldExpiry = today.AddDays(5); // still valid → Extend

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);
            SeedUser(seed, userId);
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);
            seed.FuelVouchers.Add(CustomerVoucher(voucherId, userId, purchaseOrderId, "okko", "okko-95", 50m, oldExpiry, supplierExpiry: today.AddMonths(12)));
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

        var facts = await RenewalAsync(verify, voucherId);
        facts.Branch.Should().Be("extend");
        facts.ReplacementVoucherId.Should().BeNull("an extend keeps the same paper - nothing replaced it");
        facts.TermCode.Should().Be("1m");
        facts.SurchargeUah.Should().Be(120.50m);
        facts.CustomerUserId.Should().Be(userId);
        facts.ActingUserId.Should().Be(actingUserId);
        facts.InvoiceNumber.Should().Be("INV-1");
        facts.InvoiceDate.Should().Be(today);
        facts.OldExpiration.Should().Be(oldExpiry);
        facts.NewExpiration.Should().Be(oldExpiry.AddMonths(1));

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
        var today = DateOnly.FromDateTime(TestClock.UtcNow);
        var customerExpiry = today.AddDays(5);
        // Deliberately NOT given a supplierExpiry override: both dates are the same, five days out.

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);
            SeedUser(seed, userId);
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);
            seed.FuelVouchers.Add(CustomerVoucher(voucherId, userId, purchaseOrderId, "okko", "okko-95", 50m, customerExpiry));
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
        (await verify.OrderLineItems.AsNoTracking().AnyAsync(l => l.SourceVoucherId == voucherId))
            .Should().BeFalse("no settlement for an operation that did not happen");
    }

    [Fact]
    public async Task Replace_LapsedCustomerVoucher_AssignsStockAndReturnsOldToThePool()
    {
        var userId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(TestClock.UtcNow);
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
            seed.FuelVouchers.Add(CustomerVoucher(voucherId, userId, originPurchaseId, "okko", "okko-95", 40m, today.AddDays(-3)));
            // Matching stock, valid well beyond today + 1 month.
            seed.FuelVouchers.Add(StockVoucher(stockId, "okko", "okko-95", 40m, today.AddMonths(6)));
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

        var facts = await RenewalAsync(verify, voucherId);
        facts.Branch.Should().Be("replace");
        facts.ReplacementVoucherId.Should().Be(stockId);
        facts.NewExpiration.Should().Be(today.AddMonths(1));
        facts.OldExpiration.Should().Be(today.AddDays(-3));

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
        var today = DateOnly.FromDateTime(TestClock.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);
            SeedUser(seed, userId);
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);
            seed.FuelVouchers.Add(CustomerVoucher(voucherId, userId, purchaseOrderId, "wog", "wog-95", 40m, today.AddDays(-3)));
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
        (await verify.OrderLineItems.AsNoTracking().AnyAsync(l => l.SourceVoucherId == voucherId))
            .Should().BeFalse("no renewal line for an operation that did not happen");
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
        var today = DateOnly.FromDateTime(TestClock.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);
            SeedUser(seed, userId);
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);
            seed.FuelVouchers.Add(CustomerVoucher(voucherId, userId, purchaseOrderId, "okko", "okko-95", 40m, today.AddDays(-3)));
            // Stock that cost us 50/litre: 40 L is 2000 UAH, so a 300 surcharge is far under it.
            seed.FuelVouchers.Add(StockVoucher(stockId, "okko", "okko-95", 40m, today.AddMonths(6), costPerLiter: 50m));
            // Set explicitly: fuel_types is not truncated between tests, so the opt-in test that runs
            // before or after this one must not decide the outcome here.
            await seed.FuelTypes
                .Where(f => f.Id == ToUuid("okko-95"))
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
        (await verify.OrderLineItems.AsNoTracking().AnyAsync(l => l.SourceVoucherId == voucherId))
            .Should().BeFalse("no renewal line for an operation that did not happen");
    }

    [Fact]
    public async Task Replace_SurchargeAboveStockCost_IsAccepted()
    {
        var userId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(TestClock.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);
            SeedUser(seed, userId);
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);
            seed.FuelVouchers.Add(CustomerVoucher(voucherId, userId, purchaseOrderId, "okko", "okko-95", 40m, today.AddDays(-3)));
            seed.FuelVouchers.Add(StockVoucher(stockId, "okko", "okko-95", 40m, today.AddMonths(6), costPerLiter: 50m));
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
        var today = DateOnly.FromDateTime(TestClock.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);
            SeedUser(seed, userId);
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);
            seed.FuelVouchers.Add(CustomerVoucher(voucherId, userId, purchaseOrderId, "okko", "okko-95", 40m, today.AddDays(-3)));
            seed.FuelVouchers.Add(StockVoucher(stockId, "okko", "okko-95", 40m, today.AddMonths(6), costPerLiter: 50m));
            await seed.FuelTypes
                .Where(f => f.Id == ToUuid("okko-95"))
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
        var today = DateOnly.FromDateTime(TestClock.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);
            SeedUser(seed, userId);
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);
            seed.FuelVouchers.Add(CustomerVoucher(voucherId, userId, purchaseOrderId, "okko", "okko-95", 20m, today.AddDays(10), supplierExpiry: today.AddMonths(12)));
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
        var facts = await RenewalAsync(verify, voucherId);
        facts.SurchargeUah.Should().Be(0m, "a free compensation is a zero surcharge, and it is still recorded");
    }

    [Fact]
    public async Task Confirm_UnassignedStockVoucher_RejectedAsNotCustomerVoucher()
    {
        var stockId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(TestClock.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);
            seed.FuelVouchers.Add(StockVoucher(stockId, "okko", "okko-95", 50m, today.AddMonths(3)));
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
        var today = DateOnly.FromDateTime(TestClock.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);
            SeedUser(seed, userId);
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);
            var v = CustomerVoucher(voucherId, userId, purchaseOrderId, "okko", "okko-95", 50m, today.AddDays(5));
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
        var today = DateOnly.FromDateTime(TestClock.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);
            SeedUser(seed, userId);
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);
            seed.FuelVouchers.Add(CustomerVoucher(voucherId, userId, purchaseOrderId, "okko", "okko-95", 50m, today.AddDays(5), supplierExpiry: today.AddMonths(12)));
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
        var today = DateOnly.FromDateTime(TestClock.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);
            SeedUser(seed, userId);
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);
            seed.FuelVouchers.Add(CustomerVoucher(voucherId, userId, purchaseOrderId, "okko", "okko-95", 50m, today.AddDays(5), supplierExpiry: today.AddMonths(12)));
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
        (await verify.OrderLineItems.AsNoTracking().CountAsync(l => l.SourceVoucherId == voucherId))
            .Should().Be(1, "the renewal is one line of one order");
        (await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == voucherId))
            .CustomerExpirationDate.Should().Be(today.AddDays(5).AddMonths(1));
    }

    /// <summary>
    /// The idempotency key, and the answer to a business question the review asked.
    /// <para>
    /// A customer MAY renew the same voucher any number of times - the business said so on
    /// 2026-10-20 - so a unique on the voucher alone would be wrong. The key is (voucher, the
    /// expiry it was moved to), because a repeated request produces the SAME new expiry.
    /// </para>
    /// <para>
    /// <b>Why this test writes to the table directly instead of going through the handler.</b>
    /// The handler checks the voucher's current expiry first, so a second confirmation of the
    /// same request is already refused by the application - which would make this test pass
    /// with or without the index. The index is the backstop for the other half of the race:
    /// the one where the voucher's expiry has NOT been moved yet, because the process died
    /// between the check and the write. Only the database can settle that, so only the
    /// database is asked.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TheSameRenewalToTheSameExpiry_CannotBeRecordedTwice()
    {
        var userId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(TestClock.UtcNow);
        var oldExpiry = today.AddDays(5);
        var newExpiry = oldExpiry.AddMonths(1);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);
            SeedUser(seed, userId);
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);
            seed.FuelVouchers.Add(CustomerVoucher(voucherId, userId, purchaseOrderId, "okko", "okko-95", 50m, oldExpiry, supplierExpiry: today.AddMonths(12)));
            await seed.SaveChangesAsync();
        }

        // The first renewal, recorded. It is an ORDER now (planning #319): the line carries the
        // renewal's intent and its outcome, the order carries the money.
        using (var ctx = CreateContext())
        {
            AddRenewal(ctx, voucherId, userId, oldExpiry, newExpiry);
            await ctx.SaveChangesAsync();
        }

        // The same renewal again - a double-clicked "confirm renewal" - must be refused.
        using (var ctx = CreateContext())
        {
            AddRenewal(ctx, voucherId, userId, oldExpiry, newExpiry);
            var act = () => ctx.SaveChangesAsync();

            await act.Should().ThrowAsync<DbUpdateException>(
                "the same voucher cannot be moved to the same expiry twice - that is the double-click");
        }

        // And a genuine second renewal, to a LATER expiry, is allowed - the business rule is
        // that a customer may renew as often as they like.
        using (var ctx = CreateContext())
        {
            AddRenewal(ctx, voucherId, userId, newExpiry, newExpiry.AddMonths(1));
            var act = () => ctx.SaveChangesAsync();

            await act.Should().NotThrowAsync(
                "a customer may renew the same voucher any number of times, as long as it moves forward");
        }

        using var verify = CreateContext();
        (await verify.OrderLineItems.AsNoTracking().CountAsync(l => l.SourceVoucherId == voucherId))
            .Should().Be(2);
    }

    /// <summary>
    /// A renewal, written the way the operator's path writes it now (planning #319): an order with
    /// one line, and the line carries the renewal's intent and its outcome.
    /// </summary>
    private static void AddRenewal(
        ApplicationDbContext ctx,
        Guid voucherId,
        Guid customerUserId,
        DateOnly oldExpiry,
        DateOnly newExpiration)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = customerUserId,
            Price = 10m,
            Kind = OrderKind.Renewal,
            Status = OrderStatus.Fulfilled,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow,
            LineItems = new List<OrderLineItem>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    Brand = "okko",
                    BrandId = BrandIds.ForSlug("okko"),
                    FuelTypeId = ToUuid("okko-95"),
                    Liters = 50m,
                    Quantity = 1,
                    UnitPrice = 10m,
                    LineTotal = 10m,
                    TermCode = "1m",
                    RenewalBranch = "extend",
                    SourceVoucherId = voucherId,
                    PreviousCustomerExpiration = oldExpiry,
                    NewCustomerExpiration = newExpiration
                }
            }
        };

        ctx.Orders.Add(order);
    }

    /// <summary>
    /// The renewal's facts, read where they live now (planning #319): the line carries the intent
    /// and the outcome, the order carries the money and the brand's invoice, the ledger carries the
    /// custody, and the audit event names the operator.
    /// </summary>
    private static async Task<RenewalFacts> RenewalAsync(ApplicationDbContext ctx, Guid voucherId)
    {
        var line = await ctx.OrderLineItems.AsNoTracking()
            .Include(l => l.Order)
            .FirstAsync(l => l.SourceVoucherId == voucherId);

        var replacement = await ctx.VoucherMovements.AsNoTracking()
            .Where(m => m.LineItemId == line.Id && m.Kind == VoucherMovementKinds.ReplaceOut)
            .Select(m => (Guid?)m.VoucherId)
            .FirstOrDefaultAsync();

        var actingUserId = await ctx.AuditEventOutbox.AsNoTracking()
            .Where(e => e.EventType == "VoucherRenewedByOperator")
            .OrderByDescending(e => e.ChangedAtUtc)
            .Select(e => (Guid?)e.ChangedByUserId)
            .FirstOrDefaultAsync();

        return new RenewalFacts(
            line.RenewalBranch ?? string.Empty,
            line.TermCode ?? string.Empty,
            line.PreviousCustomerExpiration,
            line.NewCustomerExpiration,
            line.Order.Price,
            line.Order.UserId,
            line.Order.ExternalInvoiceNumber,
            line.Order.ExternalInvoiceDate,
            replacement,
            actingUserId);
    }

    private sealed record RenewalFacts(
        string Branch,
        string TermCode,
        DateOnly? OldExpiration,
        DateOnly? NewExpiration,
        decimal SurchargeUah,
        Guid CustomerUserId,
        string? InvoiceNumber,
        DateOnly? InvoiceDate,
        Guid? ReplacementVoucherId,
        Guid? ActingUserId);

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
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
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
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
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
        BrandId = BrandIds.ForSlug(brand),
        Brand = brand,
        FuelTypeId =   ToUuid(fuelTypeId),
        Liters = liters,
        SupplierExpirationDate = supplierExpiry ?? expiry,
        CustomerExpirationDate = expiry,
            VoucherNumber = $"CUS-{id:N}"[..16],
            QrPayload = $"qr-{id:N}",
            Status = VoucherStatus.Assigned,
            AssignedToUserId = userId,
            OrderId = orderId,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        };

    private static FuelVoucher StockVoucher(Guid id, string brand, string fuelTypeId, decimal liters, DateOnly expiry, decimal? costPerLiter = null)
        => new()
        {
            Id = id,
            BrandId = BrandIds.ForSlug(brand),
            Brand = brand,
            FuelTypeId =   ToUuid(fuelTypeId),
            Liters = liters,
            SupplierExpirationDate = expiry,
            CustomerExpirationDate = expiry,
            VoucherNumber = $"STK-{id:N}"[..16],
            QrPayload = $"qr-{id:N}",
            Status = VoucherStatus.Available,
            CostPerLiter = costPerLiter,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        };

    private static async Task ResetDataAsync(ApplicationDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "fuel_vouchers", "users", "provider_event_outbox" RESTART IDENTITY CASCADE""");
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);
}

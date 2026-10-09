using FluentAssertions;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Renewal.Checkout;
using FuelFlow.Features.Vouchers.Renewal.Operator;
using FuelFlow.Features.Vouchers.SharedModels;
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
            seed.FuelVouchers.Add(CustomerVoucher(voucherId, userId, purchaseOrderId, "OKKO", "okko-95", 50m, oldExpiry));
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

        var audit = await verify.ProviderEventOutbox.AsNoTracking()
            .Where(e => e.AggregateId == voucherId.ToString() && e.EventType == "VoucherRenewedByOperator")
            .ToListAsync();
        audit.Should().ContainSingle();
    }

    [Fact]
    public async Task Replace_LapsedCustomerVoucher_AssignsStockAndReturnsOldToThePool()
    {
        var userId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var originPurchaseId = Guid.NewGuid();

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
                ActingUserId = Guid.NewGuid()
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

    /// <summary>
    /// The surcharge was paid off-platform, but it was still paid, so it buys time out of the voucher's
    /// remaining life and the fuel we hold is worth that much less. The self-serve path already credits
    /// it at fulfilment; the operator path used to leave the cost untouched, so the same renewal cost a
    /// different amount depending on which screen recorded it — and the blended cost stayed too high.
    /// </summary>
    [Fact]
    public async Task Extend_CreditsTheSurchargeAgainstTheVouchersCost()
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
            seed.FuelVouchers.Add(CustomerVoucher(
                voucherId, userId, purchaseOrderId, "OKKO", "okko-95", 10m, today.AddDays(5), costPerLiter: 90m));
            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            await Handler(ctx).HandleAsync(new ConfirmOperatorRenewalCommand
            {
                VoucherId = voucherId,
                TermCode = "1m",
                SurchargeUah = 100m, // 100 / 10 L = 10.00/L off a 90.00 cost
                ActingUserId = Guid.NewGuid()
            });
        }

        using var verify = CreateContext();
        var voucher = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == voucherId);
        voucher.CostPerLiter.Should().Be(80m, "the paid surcharge is worth that much less fuel to us");
    }

    [Fact]
    public async Task Replace_CreditsTheSurchargeAgainstTheReplacementAndLeavesTheReleasedSourceAlone()
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
            seed.FuelVouchers.Add(CustomerVoucher(
                voucherId, userId, purchaseOrderId, "OKKO", "okko-95", 10m, today.AddDays(-3), costPerLiter: 70m));
            seed.FuelVouchers.Add(StockVoucher(stockId, "OKKO", "okko-95", 10m, today.AddMonths(6), costPerLiter: 90m));
            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            await Handler(ctx).HandleAsync(new ConfirmOperatorRenewalCommand
            {
                VoucherId = voucherId,
                TermCode = "1m",
                SurchargeUah = 100m,
                ActingUserId = Guid.NewGuid()
            });
        }

        using var verify = CreateContext();

        var replacement = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == stockId);
        replacement.CostPerLiter.Should().Be(80m, "the customer holds this voucher now and paid for part of it");

        var released = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == voucherId);
        released.CostPerLiter.Should().Be(70m, "it went back to stock, not consumed - nobody bought anything out of it");
    }

    [Fact]
    public async Task Extend_LeavesAnUncostedVoucherUncosted()
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
            seed.FuelVouchers.Add(CustomerVoucher(voucherId, userId, purchaseOrderId, "OKKO", "okko-95", 10m, today.AddDays(5)));
            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            await Handler(ctx).HandleAsync(new ConfirmOperatorRenewalCommand
            {
                VoucherId = voucherId,
                TermCode = "1m",
                SurchargeUah = 100m,
                ActingUserId = Guid.NewGuid()
            });
        }

        using var verify = CreateContext();
        // "Never recorded" must not become a number: an invented cost would feed the blended price.
        (await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == voucherId))
            .CostPerLiter.Should().BeNull();
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
            seed.FuelVouchers.Add(CustomerVoucher(voucherId, userId, purchaseOrderId, "OKKO", "okko-95", 20m, today.AddDays(10)));
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
            seed.FuelVouchers.Add(CustomerVoucher(voucherId, userId, purchaseOrderId, "OKKO", "okko-95", 50m, today.AddDays(5)));
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

    // ---- Helpers -------------------------------------------------------------------------------

    private static ConfirmOperatorRenewalCommandHandler Handler(ApplicationDbContext ctx)
        => new(ctx, new ProviderEventService(ctx));

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

    private static FuelVoucher CustomerVoucher(Guid id, Guid userId, Guid orderId, string provider, string fuelTypeId, decimal liters, DateOnly expiry, decimal? costPerLiter = null)
        => new()
        {
            Id = id,
            Provider = provider,
            FuelTypeId = fuelTypeId,
            Liters = liters,
            ProviderExpirationDate = expiry,
            CustomerExpirationDate = expiry,
            VoucherNumber = $"CUS-{id:N}"[..16],
            QrPayload = $"qr-{id:N}",
            Status = VoucherStatus.Assigned,
            AssignedToUserId = userId,
            OrderId = orderId,
            CostPerLiter = costPerLiter,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    private static FuelVoucher StockVoucher(Guid id, string provider, string fuelTypeId, decimal liters, DateOnly expiry, decimal? costPerLiter = null)
        => new()
        {
            Id = id,
            Provider = provider,
            FuelTypeId = fuelTypeId,
            Liters = liters,
            ProviderExpirationDate = expiry,
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
            .UseNpgsql(_fixture.DbContainer.GetConnectionString())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options);
}

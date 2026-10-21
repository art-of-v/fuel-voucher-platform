using FluentAssertions;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Exchange;
using FuelFlow.Features.Vouchers.Movements;
using FuelFlow.Features.Vouchers.Renewal.Operator;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The two keys that make a voucher exchange and an operator renewal idempotent (review F-10).
/// </summary>
/// <remarks>
/// <para>
/// The handler checks first and the database refuses second. The check gives the operator a readable
/// message for the ordinary double-click; the key is the guarantee for the case the check cannot
/// cover - two operators confirming the same vouchers at the same moment, where the check and the
/// write are not one atomic step.
/// </para>
/// <para>
/// These tests are about the KEY, not about the handler: they write the rows the handler would write
/// and ask the database whether it would take a second one. A key the database does not have is not
/// an idempotency guarantee, it is a convention - and the review's F-10 was filed because the
/// columns once carried the convention and nothing else.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class ExchangeRenewalIdempotencyContractTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public ExchangeRenewalIdempotencyContractTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    [Fact]
    public async Task A_Voucher_Cannot_Be_Retired_Into_Two_Exchanges()
    {
        var oldVoucherId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var batchId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetAsync(seed);

            seed.Suppliers.Add(SeedSupplier(supplierId));
            seed.FuelVouchers.Add(Stock(oldVoucherId, supplierId));
            seed.MovementBatches.Add(new MovementBatch
            {
                Id = batchId,
                SupplierId = supplierId,
                SurchargeUah = 0m,
                OpenedByUserId = Guid.NewGuid(),
                OpenedAtUtc = TestClock.UtcNow,
                ConfirmedAtUtc = TestClock.UtcNow,
                CreatedAtUtc = TestClock.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        // The pairs live in the ledger now (planning #319): the row of the retired paper is what
        // says the paper went to the supplier, and it is the row the unique index guards.
        await using (var first = CreateContext())
        {
            first.VoucherMovements.Add(ExchangeOutMovement(batchId, oldVoucherId));
            await first.SaveChangesAsync();
        }

        await using var second = CreateContext();
        second.VoucherMovements.Add(ExchangeOutMovement(batchId, oldVoucherId));
        var act = () => second.SaveChangesAsync();

        var violation = (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>().Which;

        violation.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation,
            "a voucher retired into an exchange is retired once - the second row is the double-retire");
        violation.ConstraintName.Should().Be("ix_voucher_movements_exchange_out");
    }

    [Fact]
    public async Task A_Renewal_To_The_Same_Date_Cannot_Be_Recorded_Twice()
    {
        var voucherId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetAsync(seed);

            seed.Suppliers.Add(SeedSupplier(supplierId));
            seed.Users.Add(SeedUser(userId));
            seed.FuelVouchers.Add(Stock(voucherId, supplierId));
            await seed.SaveChangesAsync();
        }

        var today = DateOnly.FromDateTime(TestClock.UtcNow);

        await using (var first = CreateContext())
        {
            AddRenewalLine(first, userId, voucherId, today, today.AddMonths(1));
            await first.SaveChangesAsync();
        }

        await using var second = CreateContext();
        AddRenewalLine(second, userId, voucherId, today, today.AddMonths(1));
        var act = () => second.SaveChangesAsync();

        var violation = (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>().Which;

        violation.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation,
            "the same renewal to the same date is the same settlement, recorded twice");
        violation.ConstraintName.Should().Be("ix_order_line_items_renewal_once");
    }

    [Fact]
    public async Task A_Voucher_May_Be_Renewed_Again_To_A_Later_Date()
    {
        var voucherId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetAsync(seed);

            seed.Suppliers.Add(SeedSupplier(supplierId));
            seed.Users.Add(SeedUser(userId));
            seed.FuelVouchers.Add(Stock(voucherId, supplierId));
            await seed.SaveChangesAsync();
        }

        var today = DateOnly.FromDateTime(TestClock.UtcNow);

        await using (var first = CreateContext())
        {
            AddRenewalLine(first, userId, voucherId, today, today.AddMonths(1));
            await first.SaveChangesAsync();
        }

        // The business answer the key was built around: a voucher may be renewed any number of times.
        // What the key refuses is the SAME renewal twice, not a second, later renewal.
        await using var second = CreateContext();
        AddRenewalLine(second, userId, voucherId, today, today.AddMonths(2));
        var act = () => second.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    // ---- Helpers -------------------------------------------------------------------------------

    /// <summary>
    /// The customer the renewal belongs to. The order carries the customer, so the renewal cannot be
    /// written without one.
    /// </summary>
    private static User SeedUser(Guid userId)
        => new()
        {
            Id = userId,
            PhoneNumber = $"+38{userId:N}"[..20],
            IsActive = true,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        };

    private static Supplier SeedSupplier(Guid supplierId)
        => new()
        {
            Id = supplierId,
            Name = $"Idempotency supplier {supplierId:N}"[..40],
            EdrIpn = TestEdrIpns.Next(),
            IsActive = true,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        };

    private static FuelVoucher Stock(Guid id, Guid supplierId)
        => new()
        {
            Id = id,
            BrandId = BrandIds.ForSlug("okko"),
            Brand = "okko",
            FuelTypeId = ToUuid("okko-95"),
            Liters = 50m,
            SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(6)),
            CustomerExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(6)),
            VoucherNumber = $"IDEM-{id:N}"[..16],
            QrPayload = $"idem-{id:N}",
            Status = VoucherStatus.Available,
            SupplierId = supplierId,
            CostPerLiter = 30m,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        };

    private static VoucherMovement ExchangeOutMovement(Guid batchId, Guid oldVoucherId)
        => new()
        {
            Id = Guid.NewGuid(),
            BatchId = batchId,
            Kind = VoucherMovementKinds.ExchangeOut,
            VoucherId = oldVoucherId,
            FromLocation = VoucherLocations.Pool,
            ToLocation = VoucherLocations.Supplier,
            ActorKind = VoucherActorKinds.User,
            ActorUserId = Guid.NewGuid(),
            SurchargeShareUah = 0m,
            OccurredAtUtc = TestClock.UtcNow
        };

    /// <summary>
    /// A renewal, written the way the operator's path writes it now (planning #319): an order with
    /// one line, and the line carries the renewal's intent and its outcome.
    /// </summary>
    private static void AddRenewalLine(
        ApplicationDbContext ctx,
        Guid customerUserId,
        Guid voucherId,
        DateOnly oldExpiry,
        DateOnly newExpiration)
    {
        ctx.Orders.Add(new Order
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
        });
    }

    private static async Task ResetAsync(ApplicationDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "movement_batches", "purchase_batches", "voucher_imports", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "suppliers", "users", "provider_event_outbox" RESTART IDENTITY CASCADE""");
    }
}

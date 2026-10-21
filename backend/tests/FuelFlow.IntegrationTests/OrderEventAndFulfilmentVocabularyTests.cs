using FluentAssertions;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The rules the database now holds on its own, for the two places the review found it did not
/// (F-8 of the production review, 2026-10-21).
/// </summary>
/// <remarks>
/// <para>
/// Each test asks the DATABASE a question, not the application: it writes a row the schema must
/// refuse, and it asserts the refusal. Writing through EF would only prove EF sent the right
/// value; the point of the change is that the rule holds even when a future handler forgets it.
/// </para>
/// <para>
/// The order event's status vocabulary is the one the code writes - <c>OrderStatus</c> spelled by
/// <c>ToString()</c> - and the same eight names <c>orders.status</c> is already pinned to. The
/// pairing rule (<c>ck_order_events_from_to</c>) said a non-Created event must carry a
/// <c>to_status</c>; it did not say WHICH status, so a typo was accepted and written into the
/// order's history.
/// </para>
/// <para>
/// The fulfilment's uniqueness is per ORDER, not per voucher, and that is the whole point of the
/// test that says so: a renewal's extend branch delivers the same paper again under the renewal
/// order, and a voucher a customer handed back goes back to the pool and is sold to the next
/// customer - so one voucher legitimately has a live fulfilment under each order it was delivered
/// by. What must never happen is one ORDER delivering one voucher twice: the reconciliation counts
/// the rows, and the realised margin is margin per litre times that count, so a second row is a
/// second margin earned on fuel that was never handed over twice.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class OrderEventAndFulfilmentVocabularyTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public OrderEventAndFulfilmentVocabularyTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    [Fact]
    public async Task An_Order_Event_May_Not_Carry_A_Status_That_Is_Not_An_Order_Status()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await ResetAsync(context);

        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            seed.Users.Add(new User
            {
                Id = userId,
                PhoneNumber = "+380500000001",
                SettlementCode = "SET-0001",
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            });
            seed.Orders.Add(new Order
            {
                Id = orderId,
                UserId = userId,
                Price = 500m,
                Status = OrderStatus.PendingPayment,
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        // The typo the pairing rule let through: `to_status` is present, so
        // `ck_order_events_from_to` is satisfied, and the vocabulary is not.
        await using var write = CreateContext();
        var act = () => write.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO order_events (id, order_id, event_type, from_status, to_status, occurred_at_utc)
            VALUES (gen_random_uuid(), {0}, 'StatusChanged', 'PendingPayment', 'PartiallyRefundedd', NOW())
            """, orderId);

        var violation = (await act.Should().ThrowAsync<PostgresException>())
            .Which;

        violation.SqlState.Should().Be(PostgresErrorCodes.CheckViolation,
            "a status that no order ever had is not a history of this order");
        violation.ConstraintName.Should().Be("ck_order_events_statuses");
    }

    [Fact]
    public async Task An_Order_Event_May_Carry_Every_Order_Status()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await ResetAsync(context);

        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            seed.Users.Add(new User
            {
                Id = userId,
                PhoneNumber = "+380500000002",
                SettlementCode = "SET-0002",
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            });
            seed.Orders.Add(new Order
            {
                Id = orderId,
                UserId = userId,
                Price = 500m,
                Status = OrderStatus.PendingPayment,
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        // The other half of the same rule: a CHECK that refuses everything is not a vocabulary.
        var statuses = new[]
        {
            "PendingPayment", "Paid", "PendingFulfillment", "PartiallyFulfilled",
            "Fulfilled", "PartiallyRefunded", "Refunded", "Cancelled"
        };

        foreach (var status in statuses)
        {
            await using var write = CreateContext();
            var act = () => write.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO order_events (id, order_id, event_type, from_status, to_status, occurred_at_utc)
                VALUES (gen_random_uuid(), {0}, 'StatusChanged', NULL, {1}, NOW())
                """, orderId, status);

            await act.Should().NotThrowAsync($"'{status}' is an order status the code writes");
        }
    }

    [Fact]
    public async Task One_Order_May_Not_Deliver_One_Voucher_Twice()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await ResetAsync(context);

        var (orderId, voucherId) = await SeedOrderAndVoucherAsync(context);

        await using (var first = CreateContext())
        {
            await first.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO fulfillments (id, order_id, voucher_id, fulfilled_at_utc)
                VALUES (gen_random_uuid(), {0}, {1}, NOW())
                """, orderId, voucherId);
        }

        await using var second = CreateContext();
        var act = () => second.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO fulfillments (id, order_id, voucher_id, fulfilled_at_utc)
            VALUES (gen_random_uuid(), {0}, {1}, NOW())
            """, orderId, voucherId);

        var violation = (await act.Should().ThrowAsync<PostgresException>())
            .Which;

        violation.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation,
            "the reconciliation counts the rows - a second one is a second margin on the same fuel");
        violation.ConstraintName.Should().Be("ux_fulfillments_order_id_voucher_id");
    }

    [Fact]
    public async Task A_Released_Fulfilment_Does_Not_Block_The_Order_From_Delivering_It_Again()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await ResetAsync(context);

        var (orderId, voucherId) = await SeedOrderAndVoucherAsync(context);

        await using (var first = CreateContext())
        {
            await first.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO fulfillments (id, order_id, voucher_id, fulfilled_at_utc, released_at_utc, release_reason)
                VALUES (gen_random_uuid(), {0}, {1}, NOW(), NOW(), 'quantity_mismatch')
                """, orderId, voucherId);
        }

        // The released row is history - "a delivery happened, and it was undone" - and the order may
        // deliver the paper again. The index is partial on the LIVE rows for exactly that reason.
        await using var second = CreateContext();
        var act = () => second.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO fulfillments (id, order_id, voucher_id, fulfilled_at_utc)
            VALUES (gen_random_uuid(), {0}, {1}, NOW())
            """, orderId, voucherId);

        await act.Should().NotThrowAsync(
            "a released fulfilment is the record that it was undone, not a claim on the voucher");
    }

    [Fact]
    public async Task One_Voucher_May_Be_Live_Under_Two_Orders_At_Once()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await ResetAsync(context);

        var (orderId, voucherId) = await SeedOrderAndVoucherAsync(context);

        var secondOrderId = Guid.NewGuid();
        await using (var seed = CreateContext())
        {
            seed.Orders.Add(new Order
            {
                Id = secondOrderId,
                UserId = context.Orders.Select(o => o.UserId).First(),
                Price = 500m,
                Status = OrderStatus.PendingPayment,
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        await using (var first = CreateContext())
        {
            await first.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO fulfillments (id, order_id, voucher_id, fulfilled_at_utc)
                VALUES (gen_random_uuid(), {0}, {1}, NOW())
                """, orderId, voucherId);
        }

        // A renewal's extend branch delivers the SAME paper again under the renewal order, and a
        // voucher a customer handed back goes back to the pool and is sold to the next customer. A
        // uniqueness rule over the voucher alone would refuse both, and the second refusal would be
        // a renewal that cannot be recorded.
        await using var second = CreateContext();
        var act = () => second.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO fulfillments (id, order_id, voucher_id, fulfilled_at_utc)
            VALUES (gen_random_uuid(), {0}, {1}, NOW())
            """, secondOrderId, voucherId);

        await act.Should().NotThrowAsync(
            "the same paper delivered by two different orders is what a renewal and a resale are");
    }

    [Fact]
    public async Task The_Refunds_View_Ext_Ref_Is_Bounded()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        // The view's ext_ref is the order's key (varchar(150)) or the order's id as text (36
        // characters) - coalescing the two gave a TEXT column, the one unbounded varchar the schema
        // had left. The cast says what the values already are.
        var lengths = await context.Database
            .SqlQueryRaw<int>(
                """
                SELECT character_maximum_length FROM information_schema.columns
                WHERE table_schema='public' AND table_name='refunds' AND column_name='ext_ref'
                """)
            .ToListAsync();

        lengths.Should().ContainSingle();
        lengths[0].Should().Be(150,
            "neither the order's key nor its id can be longer than that, so the bound costs nothing");
    }

    [Fact]
    public async Task No_View_Carries_An_Unbounded_Varchar()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        // The property is about the whole schema, not about one column: a view that grows an
        // unbounded column is the same hole the tables no longer have.
        var unbounded = await context.Database
            .SqlQueryRaw<string>(
                """
                SELECT c.relname || '.' || a.attname
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
                WHERE n.nspname = 'public' AND c.relkind = 'v'
                  AND a.atttypid = 'varchar'::regtype AND a.atttypmod = -1
                """)
            .ToListAsync();

        unbounded.Should().BeEmpty(
            "a varchar with no upper bound is a column that can grow without limit - in a view, " +
            "the same hole the tables closed");
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static async Task<(Guid OrderId, Guid VoucherId)> SeedOrderAndVoucherAsync(
        ApplicationDbContext context)
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();

        context.Users.Add(new User
        {
            Id = userId,
            PhoneNumber = "+380500000003",
            SettlementCode = "SET-0003",
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });
        context.Orders.Add(new Order
        {
            Id = orderId,
            UserId = userId,
            Price = 500m,
            Status = OrderStatus.PendingPayment,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });
        context.FuelVouchers.Add(new FuelVoucher
        {
            Id = voucherId,
            BrandId = BrandIds.Okko,
            Brand = "okko",
            FuelTypeId = ToUuid("okko-95"),
            Liters = 50m,
            SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(6)),
            CustomerExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(6)),
            VoucherNumber = "GUARD-0001",
            QrPayload = "guard-0001",
            Status = VoucherStatus.Available,
            CostPerLiter = 30m,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        await context.SaveChangesAsync();

        return (orderId, voucherId);
    }

    private static async Task ResetAsync(ApplicationDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE "order_events", "fulfillments", "fuel_vouchers", "orders", "users"
            RESTART IDENTITY CASCADE
            """);
    }
}

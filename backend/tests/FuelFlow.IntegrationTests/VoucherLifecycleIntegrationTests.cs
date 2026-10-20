using FluentAssertions;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Settings.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Events;
using FuelFlow.Features.Vouchers.GetAdminVoucherLifecycle;
using FuelFlow.Features.Vouchers.Movements;
using FuelFlow.Features.Vouchers.Renewal;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The reader D-13 was built for, against real PostgreSQL.
/// <para>
/// Nineteen writers record every handover and transition and nothing read them. This proves the one
/// question that was unanswerable for every voucher in the system — "when did this become sellable and
/// who said so" — is now answerable, and that the three records it comes from arrive interleaved
/// rather than as three separate lists.
/// </para>
/// <para>
/// Real Postgres rather than the in-memory brand because the point is the merge: the ledger rows are
/// seeded directly, as real writers would have left them, and the reader has to put them in the right
/// place in time.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public sealed class VoucherLifecycleIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public VoucherLifecycleIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// The three sources, interleaved. A movement and an event written in the same statement land at
    /// the same instant, which is also why the order is stable between calls rather than arbitrary.
    /// </summary>
    [Fact]
    public async Task The_Timeline_Merges_Custody_State_And_Money_In_Time_Order()
    {
        var userId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();
        var (t1, t2, t3, t4) = (10, 20, 30, 40);

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            SeedUser(seed, userId, "Olena");
            seed.FuelVouchers.Add(Stock(voucherId, 50m));

            // Two writes at the same instant, the way a real writer leaves them: the buy claim flips the
            // status AND moves the paper in one statement.
            seed.VoucherMovements.Add(new VoucherMovement
            {
                Id = Guid.NewGuid(),
                Kind = VoucherMovementKinds.Issued,
                VoucherId = voucherId,
                FromLocation = VoucherLocations.Pool,
                ToLocation = VoucherLocations.Customer,
                ActorKind = VoucherActorKinds.User,
                ActorUserId = userId,
                OccurredAtUtc = Base.AddMinutes(t2)
            });
            seed.VoucherEvents.Add(new VoucherEvent
            {
                Id = Guid.NewGuid(),
                VoucherId = voucherId,
                EventType = VoucherEventTypes.StatusChanged,
                FromStatus = nameof(VoucherStatus.Available),
                ToStatus = nameof(VoucherStatus.Assigned),
                ActorKind = VoucherActorKinds.User,
                ActorUserId = userId,
                OccurredAtUtc = Base.AddMinutes(t2)
            });

            seed.VoucherEvents.Add(new VoucherEvent
            {
                Id = Guid.NewGuid(),
                VoucherId = voucherId,
                EventType = VoucherEventTypes.StatusChanged,
                FromStatus = nameof(VoucherStatus.Assigned),
                ToStatus = nameof(VoucherStatus.Used),
                ActorKind = VoucherActorKinds.User,
                ActorUserId = userId,
                OccurredAtUtc = Base.AddMinutes(t3)
            });

            // The one row the ledger cannot carry: money the customer handed over. No movement and no
            // status change, so if this went missing the timeline would look complete and be wrong.
            await SeedPaidRenewalAsync(seed, voucherId, userId, Base.AddMinutes(t4), 500m);

            await seed.SaveChangesAsync();
        }

        var result = await Handler().HandleAsync(new GetAdminVoucherLifecycleQuery(voucherId));

        result.Should().NotBeNull();
        result!.VoucherId.Should().Be(voucherId);
        result.Liters.Should().Be(50m);
        result.RootVoucherId.Should().Be(voucherId, "nothing replaced this voucher");
        result.Lineage.Should().ContainSingle();

        var order = result.Entries.Select(e => e.OccurredAtUtc).ToList();
        order.Should().BeInAscendingOrder("a timeline that is not in time order is not a timeline");

        // Time dominates the sort, so the two rows written at the same instant sit together and the
        // redemption that came later stays later. Within one instant, `event` sorts before `movement`,
        // which is what makes the order stable between calls rather than arbitrary.
        result.Entries.Select(e => e.Source).Should().Equal(
        [
            VoucherLifecycleSources.Event,
            VoucherLifecycleSources.Movement,
            VoucherLifecycleSources.Event,
            VoucherLifecycleSources.Money
        ]);

        var movement = result.Entries.Single(e => e.Source == VoucherLifecycleSources.Movement);
        movement.Type.Should().Be(VoucherMovementKinds.Issued);
        movement.From.Should().Be(VoucherLocations.Pool);
        movement.To.Should().Be(VoucherLocations.Customer);
        movement.ActorName.Should().Be("Olena K", "the page must be able to say WHO, not only an id");

        var redemption = result.Entries.Single(e =>
            e.Source == VoucherLifecycleSources.Event && e.To == nameof(VoucherStatus.Used));
        redemption.ActorUserId.Should().Be(userId);

        var money = result.Entries.Single(e => e.Source == VoucherLifecycleSources.Money);
        money.Type.Should().Be("Renewal");
        money.AmountUah.Should().Be(500m, "the ledger never carries the price, so this is the only place it can be");
        money.From.Should().Be("extended", "an extend keeps the voucher's id; a replace would say replaced");

        // Movements must not invent a price. order_line_items owns it.
        movement.AmountUah.Should().BeNull();
    }

    /// <summary>
    /// A replace issues a DIFFERENT paper for the same fuel, so the renewal that produced the requested
    /// voucher is keyed on the voucher it replaced — invisible unless the chain is walked first. This is
    /// the case where reading only the requested voucher's own rows would silently drop a payment.
    /// </summary>
    [Fact]
    public async Task The_Timeline_Spans_The_Whole_Lineage_After_A_Replace()
    {
        var userId = Guid.NewGuid();
        var originalId = Guid.NewGuid();
        var replacementId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            SeedUser(seed, userId, "Olena");
            seed.FuelVouchers.Add(Stock(originalId, 50m));
            seed.FuelVouchers.Add(Stock(replacementId, 50m));

            // The renewal, keyed on the ORIGINAL as its source and delivering the REPLACEMENT.
            await SeedPaidRenewalAsync(seed, originalId, userId, Base.AddMinutes(60), 700m,
                fulfilledVoucherId: replacementId);

            await seed.SaveChangesAsync();
        }

        var result = await Handler().HandleAsync(new GetAdminVoucherLifecycleQuery(replacementId));

        result.Should().NotBeNull();
        result!.RootVoucherId.Should().Be(originalId, "the fuel this paper carries came from that paper");
        result.Lineage.Should().Equal([originalId, replacementId], because: "oldest first");

        // The money is keyed on the original, and it must still appear — this is the assertion that fails
        // if the lineage is not expanded before the renewals are read.
        var money = result.Entries.SingleOrDefault(e => e.Source == VoucherLifecycleSources.Money);
        money.Should().NotBeNull("a renewal the chain inherited is part of this fuel's history");
        money!.AmountUah.Should().Be(700m);
        money.From.Should().Be("replaced", "a different paper was issued, so this is not an extension");
    }

    /// <summary>An unknown id is a 404, not an empty timeline that looks like a real answer.</summary>
    [Fact]
    public async Task An_Unknown_Voucher_Is_Not_An_Empty_Timeline()
    {
        await using var seed = CreateContext();
        await ResetAsync(seed);

        (await Handler().HandleAsync(new GetAdminVoucherLifecycleQuery(Guid.NewGuid())))
            .Should().BeNull("an empty list for a voucher that does not exist reads as 'nothing happened'");

        (await Handler().HandleAsync(new GetAdminVoucherLifecycleQuery(Guid.Empty)))
            .Should().BeNull();
    }

    /// <summary>
    /// An archived voucher is the one an operator most wants to look up, and the archive flag has its
    /// own event. Returning 404 would hide exactly the row the page exists for.
    /// </summary>
    [Fact]
    public async Task An_Archived_Voucher_Still_Answers_With_Its_Flag_Set()
    {
        var voucherId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            var voucher = Stock(voucherId, 50m);
            voucher.IsDeleted = true;
            seed.FuelVouchers.Add(voucher);
            seed.VoucherEvents.Add(new VoucherEvent
            {
                Id = Guid.NewGuid(),
                VoucherId = voucherId,
                EventType = VoucherEventTypes.Archived,
                ActorKind = VoucherActorKinds.System,
                OccurredAtUtc = Base.AddMinutes(5)
            });
            await seed.SaveChangesAsync();
        }

        var result = await Handler().HandleAsync(new GetAdminVoucherLifecycleQuery(voucherId));

        result.Should().NotBeNull();
        result!.IsArchived.Should().BeTrue();
        result.Entries.Should().ContainSingle(e =>
            e.Source == VoucherLifecycleSources.Event && e.Type == VoucherEventTypes.Archived);
    }

    /// <summary>A scheduler's author resolves to nothing, and the row is still there and still answerable.</summary>
    [Fact]
    public async Task A_System_Author_Keeps_Its_Kind_With_No_Id_And_No_Invented_Name()
    {
        var voucherId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            await ResetAsync(seed);
            seed.FuelVouchers.Add(Stock(voucherId, 50m));
            seed.VoucherEvents.Add(new VoucherEvent
            {
                Id = Guid.NewGuid(),
                VoucherId = voucherId,
                EventType = VoucherEventTypes.StatusChanged,
                FromStatus = nameof(VoucherStatus.Available),
                ToStatus = nameof(VoucherStatus.Expired),
                ActorKind = VoucherActorKinds.System,
                ActorUserId = null,
                OccurredAtUtc = Base.AddMinutes(5)
            });
            await seed.SaveChangesAsync();
        }

        var entry = (await Handler().HandleAsync(new GetAdminVoucherLifecycleQuery(voucherId)))!
            .Entries.Single();

        entry.ActorKind.Should().Be(VoucherActorKinds.System);
        entry.ActorUserId.Should().BeNull();
        entry.ActorName.Should().BeNull(
            "actor_user_id is FK-free so the audit outlives its author; a placeholder would invent a person");
    }

    // ---- Fixture ------------------------------------------------------------------------------

    private static DateTime Base => new(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc);

    private GetAdminVoucherLifecycleQueryHandler Handler() => new(CreateContext());

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    private static FuelVoucher Stock(Guid id, decimal liters) => new()
    {
        Id = id,
        Brand = "okko",
        FuelTypeId = ToUuid("okko-95"),
        Liters = liters,
        SupplierExpirationDate = new DateOnly(2027, 1, 1),
        CustomerExpirationDate = new DateOnly(2027, 1, 1),
        VoucherNumber = $"V-{id:N}"[..20],
        QrPayload = $"qr-{id:N}",
        Status = VoucherStatus.Available,
        CostPerLiter = 40m,
        CreatedAtUtc = Base,
        UpdatedAtUtc = Base
    };

    private static void SeedUser(ApplicationDbContext seed, Guid userId, string firstName)
        => seed.Users.Add(new User
        {
            Id = userId,
            PhoneNumber = $"+38{userId:N}"[..20],
            FirstName = firstName,
            LastName = "K",
            IsActive = true,
            CreatedAtUtc = Base,
            UpdatedAtUtc = Base
        });

    /// <summary>
    /// A paid renewal as the self-serve checkout leaves it: an order line carrying the term, the expiry
    /// pair, the money, and a fulfilment naming which voucher was actually handed over.
    /// </summary>
    private static async Task SeedPaidRenewalAsync(
        ApplicationDbContext seed,
        Guid sourceVoucherId,
        Guid userId,
        DateTime at,
        decimal amount,
        Guid? fulfilledVoucherId = null)
    {
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();

        seed.Orders.Add(new Order
        {
            Id = orderId,
            UserId = userId,
            Price = amount,
            Kind = OrderKind.Renewal,
            Status = OrderStatus.Fulfilled,
            MonobankInvoiceId = $"inv-{orderId:N}",
            CreatedAtUtc = at.AddMinutes(-5),
            UpdatedAtUtc = at,
            FulfilledAtUtc = at
        });

        seed.OrderLineItems.Add(new OrderLineItem
        {
            Id = lineId,
            OrderId = orderId,
            Brand = "okko",
            FuelTypeId = ToUuid("okko-95"),
            Liters = 50m,
            Quantity = 1,
            UnitPrice = amount,
            LineTotal = amount,
            TermCode = "1m",
            SourceVoucherId = sourceVoucherId,
            RenewalBranch = VoucherRenewalBranchCodes.Extend,
            PreviousCustomerExpiration = new DateOnly(2026, 4, 30),
            NewCustomerExpiration = new DateOnly(2026, 5, 30)
        });

        await seed.SaveChangesAsync();

        // Only a replace delivers a different paper; an extend keeps the same id and writes no fulfilment.
        if (fulfilledVoucherId is { } delivered)
        {
            seed.Fulfillments.Add(new Fulfillment
            {
                OrderId = orderId,
                VoucherId = delivered,
                LineItemId = lineId,
                FulfilledAtUtc = at
            });
        }
    }

    private static async Task ResetAsync(ApplicationDbContext context)
        => await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "voucher_events", "voucher_movements", "payments", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");
}

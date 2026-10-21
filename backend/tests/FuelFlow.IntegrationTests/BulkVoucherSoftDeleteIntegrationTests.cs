using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.BulkActionVouchers;
using FuelFlow.Features.Vouchers.DeleteVoucher;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Features.Vouchers.Events;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Bulk deletion of vouchers is soft, never hard (#217).
/// </summary>
/// <remarks>
/// <para>
/// Owner policy: soft delete everywhere, always. Only garbage that a cleanup job removes may be
/// hard-deleted, and a voucher is never garbage - it may carry a cost an operator typed, be the
/// surrendered half of an exchange, or be the stock a valuation was computed from.
/// </para>
/// <para>
/// The old implementation was hard, and accidentally guarded rather than safe: four RESTRICT
/// foreign keys made it throw for any voucher that had been fulfilled, exchanged or renewed, so it
/// destroyed precisely the vouchers that had no history and refused the rest with a raw FK error.
/// These tests pin the corrected behaviour, including that the FK failure mode is gone.
/// </para>
/// <para>
/// Real PostgreSQL throughout. An in-memory brand supports neither <c>ExecuteUpdate</c> nor the
/// foreign keys, so a unit test here could have proved nothing about either.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class BulkVoucherSoftDeleteIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public BulkVoucherSoftDeleteIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

[Fact]
    public async Task DeleteAll_Flags_The_Vouchers_Instead_Of_Removing_The_Rows()
    {
        await SeedAsync();
        var context = CreateContext();

        var result = await Handler(context).HandleAsync(new BulkActionVouchersCommand("delete_all", null, null));

        result.Success.Should().BeTrue();
        result.Count.Should().Be(2);

        // The rows are still there. That is the whole point: history outlives the operator's
        // decision to remove a voucher from the shelf.
        (await context.FuelVouchers.IgnoreQueryFilters().CountAsync()).Should().Be(2);
        (await context.FuelVouchers.IgnoreQueryFilters().CountAsync(v => v.IsDeleted)).Should().Be(2);

        // And they are gone from every ordinary query, via the global filter.
        (await context.FuelVouchers.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Delete_By_Selection_Flags_Only_Those_Selected()
    {
        await SeedAsync();
        var context = CreateContext();
        var all = await context.FuelVouchers.IgnoreQueryFilters().ToListAsync();
        var target = all[0];

        var result = await Handler(context).HandleAsync(
            new BulkActionVouchersCommand("delete", [target.Id], null));

        result.Success.Should().BeTrue();
        var deleted = await context.FuelVouchers.IgnoreQueryFilters()
            .Where(v => v.IsDeleted)
            .Select(v => v.Id)
            .ToListAsync();

        deleted.Should().ContainSingle().Which.Should().Be(target.Id);
    }

    // The old hard delete threw a raw foreign-key error here. Soft delete has no such constraint,
    // so an operator can remove a voucher that is part of history instead of being handed an
    // exception from deep inside the database driver.
    [Fact]
    public async Task A_Voucher_That_History_References_Can_Still_Be_Soft_Deleted()
    {
        await SeedAsync();
        var context = CreateContext();
        var voucher = await context.FuelVouchers.IgnoreQueryFilters().FirstAsync();

        var userId = Guid.NewGuid();
        context.Users.Add(new User { Id = userId, PhoneNumber = "+380000007777", CreatedAtUtc = TestClock.UtcNow });
        context.Orders.Add(new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Price = 100m,
            Status = OrderStatus.Fulfilled,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });
        await context.SaveChangesAsync();

        context.Fulfillments.Add(new Fulfillment
        {
            OrderId = (await context.Orders.FirstAsync()).Id,
            VoucherId = voucher.Id
        });
        await context.SaveChangesAsync();

        // Fresh context for the action, as a request would get from DI.
        var acting = CreateContext();
        var result = await Handler(acting).HandleAsync(
            new BulkActionVouchersCommand("delete", [voucher.Id], null));

        result.Success.Should().BeTrue("deleting a voucher must not fail because history points at it");

        // The fulfillment is still there - it is history, and deleting a voucher must not take it.
        (await context.Fulfillments.CountAsync()).Should().Be(1);
        (await acting.FuelVouchers.IgnoreQueryFilters()
            .Where(v => v.Id == voucher.Id && v.IsDeleted)
            .ToListAsync())
            .Should().ContainSingle();
    }

    // The rule a paid voucher is never deleted, stated explicitly rather than left standing on a
// RESTRICT foreign key that #217 removed by going soft.
//
// Before, a voucher's safety came from an accident: the hard delete threw for anything with
// history. Soft delete fixed the real problem - the row survives - but took the accident with it,
// so both delete paths went on archiving paid vouchers silently.

[Theory]
[InlineData(VoucherStatus.Assigned)]
[InlineData(VoucherStatus.Used)]
[InlineData(VoucherStatus.Expired)]
[InlineData(VoucherStatus.Blocked)]
[InlineData(VoucherStatus.Deactivated)]
public async Task Bulk_Delete_Refuses_A_Voucher_That_Is_Not_Just_Stock(VoucherStatus status)
{
    await SeedAsync();
    var context = CreateContext();
    var voucher = await context.FuelVouchers.IgnoreQueryFilters().FirstAsync();
    voucher.Status = status;

    // ck_voucher_held_has_order requires an order for Assigned/Used/Blocked, so a realistic row in
    // one of those states always carries one. Expired and Deactivated need no order, which leaves
    // them covering the guard's status signal with nothing else to lean on.
    if (status is VoucherStatus.Assigned or VoucherStatus.Used or VoucherStatus.Blocked)
    {
        voucher.OrderId = AddOrder(context);
    }

    await context.SaveChangesAsync();

    var acting = CreateContext();
    var result = await Handler(acting).HandleAsync(
        new BulkActionVouchersCommand("delete", [voucher.Id], null));

    result.Success.Should().BeFalse();
    result.Error.Should().NotBeNullOrWhiteSpace("the admin has to be told why, not just refused");
    (await acting.FuelVouchers.IgnoreQueryFilters().Where(v => v.IsDeleted).ToListAsync()).Should().BeEmpty();
}

[Fact]
public async Task Bulk_Delete_Refuses_A_Voucher_That_Carries_An_Order()
{
    // Status alone is not the signal - a replace releases ownership but keeps order_id on purpose,
    // so a voucher can be Available and still be history.
    await SeedAsync();
    var context = CreateContext();
    var voucher = await context.FuelVouchers.IgnoreQueryFilters().FirstAsync();

    voucher.OrderId = AddOrder(context);
    voucher.Status = VoucherStatus.Available; // still stock-looking, but it belongs to an order
    await context.SaveChangesAsync();

    var acting = CreateContext();
    var result = await Handler(acting).HandleAsync(
        new BulkActionVouchersCommand("delete", [voucher.Id], null));

    result.Success.Should().BeFalse("order_id is the signal, whatever the status says");
}

[Fact]
public async Task Bulk_Delete_Refuses_The_Whole_Batch_And_Changes_Nothing()
{
    // The dangerous shape: an admin selects 50 stock vouchers, 3 of which are paid. Archiving the
    // other 47 would still report the right count while quietly burying 3 paid vouchers, so the
    // whole batch is refused.
    await SeedAsync();
    var context = CreateContext();
    var all = await context.FuelVouchers.IgnoreQueryFilters().ToListAsync();
    all[0].Status = VoucherStatus.Used;
    all[0].OrderId = AddOrder(context);
    await context.SaveChangesAsync();

    var acting = CreateContext();
    var result = await Handler(acting).HandleAsync(
        new BulkActionVouchersCommand("delete", all.Select(v => v.Id).ToList(), null));

    result.Success.Should().BeFalse();
    result.Error.Should().Contain("1 of"); // names how many are the problem
    (await acting.FuelVouchers.IgnoreQueryFilters().Where(v => v.IsDeleted).ToListAsync()).Should().BeEmpty(
        "a partial delete would hide which vouchers were actually refused");
}

[Fact]
public async Task Bulk_Delete_Of_Pure_Stock_Still_Works()
{
    await SeedAsync();
    var context = CreateContext();
    var all = await context.FuelVouchers.IgnoreQueryFilters().ToListAsync();

    var acting = CreateContext();
    var result = await Handler(acting).HandleAsync(
        new BulkActionVouchersCommand("delete", all.Select(v => v.Id).ToList(), null));

    result.Success.Should().BeTrue("stock that never left stock is still archivable");
    (await acting.FuelVouchers.IgnoreQueryFilters().Where(v => v.IsDeleted).ToListAsync()).Should().HaveCount(all.Count);
}

private BulkActionVouchersCommandHandler Handler(ApplicationDbContext context)
        => new(
            context,
            new Mock<IBackgroundJobClient>().Object,
            new AuditEventService(context));

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

[Fact]
public async Task Delete_All_Archives_Stock_And_Names_What_It_Kept()
{
    // delete_all reaches every voucher in the system, and the admin confirms it with the global
    // total - so unfiltered it archived the paid ones too and called it success. That is #284 on
    // the most dangerous path of the three.
    await SeedAsync();
    var context = CreateContext();
    var all = await context.FuelVouchers.IgnoreQueryFilters().ToListAsync();
    all[0].Status = VoucherStatus.Used;
    all[0].OrderId = AddOrder(context);
    await context.SaveChangesAsync();

    var acting = CreateContext();
    var result = await Handler(acting).HandleAsync(new BulkActionVouchersCommand("delete_all", null, null));

    // Reported as a failure on purpose: the stock went, but the list did not empty, and an admin
    // has to be told which is which rather than left to infer it.
    result.Success.Should().BeFalse();
    result.Error.Should().Contain("kept 1");

    var stillThere = await acting.FuelVouchers.IgnoreQueryFilters().Where(v => !v.IsDeleted).ToListAsync();
    stillThere.Should().ContainSingle("the paid voucher is the record of what the customer bought");
    stillThere[0].Id.Should().Be(all[0].Id);

    // And the stock did get archived - otherwise the feature is just broken.
    (await acting.FuelVouchers.IgnoreQueryFilters().CountAsync(v => v.IsDeleted)).Should().Be(1);
}

/// <summary>
/// D-13. <c>delete_all</c> is the most destructive button in the admin and was the one writer left
/// with no record: the audit row it wrote carried a COUNT and a scope of "all", so nothing anywhere
/// said <em>which</em> vouchers one click put in a drawer.
/// <para>
/// The batching is the other half. Recording the events needs the ids, and one SaveChanges over 50k
/// of them rebuilds the memory spike the set-based form was written to avoid - so the ids come back
/// 500 at a time, the same shape <c>ExpiredVoucherLossService</c> uses. Asserted here by seeding
/// more than one batch's worth, because a test that only ever exercises the single-batch path would
/// pass with the loop removed.
/// </para>
/// </summary>
[Fact]
public async Task Delete_All_Records_An_Archived_Event_For_Every_Voucher_It_Archived()
{
    await SeedAsync();
    var context = CreateContext();

    // More than ArchiveBatchSize, so the drain loop has to make a second pass. The archived rows leave
    // the !IsDeleted filter, which is what makes each pass see fresh candidates.
    var importId = (await context.VoucherImports.FirstAsync()).Id;
    var fuel = await context.FuelTypes.FirstAsync();
    var supplier = await context.Suppliers.FirstAsync();
    for (var i = 0; i < 620; i++)
    {
        context.FuelVouchers.Add(new FuelVoucher
        {
            Id = Guid.NewGuid(),
            ImportJobId = importId,
            FuelTypeId = fuel.Id,
            SupplierId = supplier.Id,
            BrandId = BrandIds.ForSlug("okko"),
            Brand = "okko",
            VoucherNumber = $"V-{Guid.NewGuid():N}",
            QrPayload = Guid.NewGuid().ToString(),
            Liters = 10m,
            CostPerLiter = 40m,
            Status = VoucherStatus.Imported,
            CreatedAtUtc = TestClock.UtcNow
        });
    }

    await context.SaveChangesAsync();

    var expected = await context.FuelVouchers.CountAsync();
    var operatorId = Guid.NewGuid();

    var acting = CreateContext();
    var result = await Handler(acting).HandleAsync(
        new BulkActionVouchersCommand("delete_all", null, null, operatorId, "Admin User"));

    result.Success.Should().BeTrue("nothing paid was seeded, so nothing was kept");

    var verify = CreateContext();
    (await verify.FuelVouchers.IgnoreQueryFilters().CountAsync(v => v.IsDeleted))
        .Should().Be(expected, "every voucher was archived, across more than one batch");

    // One event per archived voucher - the thing the count-only audit row could never answer.
    var events = await verify.VoucherEvents.AsNoTracking().ToListAsync();
    events.Should().HaveCount(expected);

    events.Should().OnlyContain(e =>
        e.EventType == VoucherEventTypes.Archived &&
        e.ActorKind == VoucherActorKinds.User &&
        e.ActorUserId == operatorId);

    // Archived is not a status change, so there is no from/to to invent - and the events name exactly
    // the vouchers that were archived, not a superset.
    events.Should().OnlyContain(e => e.FromStatus == null && e.ToStatus == null);
    (await verify.FuelVouchers.IgnoreQueryFilters()
        .Where(v => v.IsDeleted)
        .Select(v => v.Id)
        .ToListAsync())
        .Should().BeEquivalentTo(events.Select(e => e.VoucherId),
            "an event for a voucher that was not archived would be a claim that never happened");
}

/// <summary>
/// The re-run case the batching gives up atomicity for. An archived row leaves the candidate filter, so
/// a second press picks up where the first stopped rather than re-archiving or erroring — which is why
/// the operation is safe to run twice.
/// </summary>
[Fact]
public async Task Delete_All_Run_Twice_Archives_Nothing_Second_Time_And_Records_Nothing_Either()
{
    await SeedAsync();
    var first = CreateContext();
    var operatorId = Guid.NewGuid();

    (await Handler(first).HandleAsync(
        new BulkActionVouchersCommand("delete_all", null, null, operatorId, "Admin User")))
        .Success.Should().BeTrue();

    var afterFirst = await first.VoucherEvents.AsNoTracking().CountAsync();

    var second = CreateContext();
    (await Handler(second).HandleAsync(
        new BulkActionVouchersCommand("delete_all", null, null, operatorId, "Admin User")))
        .Count.Should().Be(0, "the first run already archived everything still visible");

    // The second run found nothing to do, so it wrote nothing: an event for a voucher nothing was
    // done to would be the exact defect this whole table exists to prevent.
    (await second.VoucherEvents.AsNoTracking().CountAsync()).Should().Be(afterFirst);
}

[Fact]
public async Task Single_Delete_Refuses_A_Paid_Voucher()
{
    // The other half of the rule: the admin endpoint that deletes one voucher, which is not the
    // bulk handler and does not share its code.
    await SeedAsync();
    var context = CreateContext();
    var voucher = await context.FuelVouchers.IgnoreQueryFilters().FirstAsync();
    voucher.Status = VoucherStatus.Used;
    voucher.OrderId = AddOrder(context);
    await context.SaveChangesAsync();

    var acting = CreateContext();
    var result = await new DeleteVoucherCommandHandler(acting, new AuditEventService(acting)).HandleAsync(
        new DeleteVoucherCommand(voucher.Id, Guid.NewGuid(), "admin"));

    result.Should().NotBeNull();
    result!.Success.Should().BeFalse();
    result.Error.Should().NotBeNullOrWhiteSpace();
    (await acting.FuelVouchers.IgnoreQueryFilters().Where(v => v.IsDeleted).ToListAsync()).Should().BeEmpty();
}

[Fact]
public async Task Single_Delete_Of_Stock_Still_Archives_It()
{
    await SeedAsync();
    var context = CreateContext();
    var voucher = await context.FuelVouchers.IgnoreQueryFilters().FirstAsync();

    var acting = CreateContext();
    var result = await new DeleteVoucherCommandHandler(acting, new AuditEventService(acting)).HandleAsync(
        new DeleteVoucherCommand(voucher.Id, Guid.NewGuid(), "admin"));

    result.Should().NotBeNull();
    result!.Success.Should().BeTrue();
    (await acting.FuelVouchers.IgnoreQueryFilters().Where(v => v.IsDeleted).ToListAsync())
        .Should().ContainSingle();
}

private static int _phoneCounter;

// Attaches a user and a fulfilled order and returns the order id. Deliberately does not save: the
// caller saves once, so a voucher whose status is being set in the same breath lands atomically.
// A save in here would flush a half-built voucher and trip ck_voucher_held_has_order.
private Guid AddOrder(ApplicationDbContext context)
    {
        // An order needs a user, and the phone number is unique, so each call takes the next one
        // rather than repeating a literal that would collide with the row an earlier test left.
        var userId = Guid.NewGuid();
        context.Users.Add(new User
        {
            Id = userId,
            PhoneNumber = "+380" + (20000000 + Interlocked.Increment(ref _phoneCounter)),
            CreatedAtUtc = TestClock.UtcNow
        });

        var orderId = Guid.NewGuid();
        context.Orders.Add(new Order
        {
            Id = orderId,
            UserId = userId,
            Price = 100m,
            Status = OrderStatus.Fulfilled,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        return orderId;
    }

private async Task SeedAsync()
    {
        var seed = CreateContext();
        await ResetAsync(seed);

        if (!await seed.Suppliers.AnyAsync())
        {
            seed.Suppliers.Add(new Supplier
            {
                EdrIpn = TestEdrIpns.Next(),
                Id = Guid.NewGuid(),
                Name = "S",
                IsActive = true,
                CreatedAtUtc = TestClock.UtcNow
            });
        }
        if (!await seed.FuelTypes.AnyAsync())
        {
            seed.FuelTypes.Add(new FuelTypeEntity
            {
                Id = ToUuid("okko-dp"),
                Name = "Дизель",
                BrandId = BrandIds.Okko,
                BasePrice = 60m,
                DiscountPrice = 60m,
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            });
        }

        seed.VoucherImports.Add(new VoucherImport
        {
            Id = Guid.NewGuid(),
            FileName = "soft.pdf",
            CompletedAtUtc = TestClock.UtcNow,
            Status = "Completed",
            StartedAtUtc = TestClock.UtcNow
        });
        await seed.SaveChangesAsync();

        var importId = (await seed.VoucherImports.FirstAsync()).Id;
        var fuel = await seed.FuelTypes.FirstAsync();
        var supplier = await seed.Suppliers.FirstAsync();

        foreach (var cost in new[] { 40m, 60m })
        {
            seed.FuelVouchers.Add(new FuelVoucher
            {
                Id = Guid.NewGuid(),
                ImportJobId = importId,
                FuelTypeId = fuel.Id,
                SupplierId = supplier.Id,
                BrandId = BrandIds.ForSlug("okko"),
                Brand = "okko",
                VoucherNumber = $"V-{Guid.NewGuid():N}",
                QrPayload = Guid.NewGuid().ToString(),
                Liters = 10m,
                CostPerLiter = cost,
                Status = VoucherStatus.Imported,
                CreatedAtUtc = TestClock.UtcNow
            });
        }

await seed.SaveChangesAsync();
    }

    private static async Task ResetAsync(ApplicationDbContext context)
        => await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "payments", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "movement_batches", "company_invitations", "company_members", "legal_entities", "verification_codes", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");
}

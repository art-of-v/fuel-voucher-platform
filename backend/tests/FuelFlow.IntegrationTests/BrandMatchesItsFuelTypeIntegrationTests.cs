using FluentAssertions;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The brand key on a row is the brand of the fuel it claims, and the database says so.
/// </summary>
/// <remarks>
/// <para>
/// Four tables carry a brand as a name - <c>fuel_vouchers</c>, <c>order_line_items</c>,
/// <c>purchase_batches</c>, and the fourth was <c>voucher_exchanges</c> until the fold of #319 put the
/// exchange's pairs in the ledger - and each of them also names the fuel the row
/// is about. Until now only one of those two copies was constrained, and a disagreement was
/// invisible rather than visibly wrong: the voucher simply stopped being sellable, with no error
/// anywhere. <c>BrandFuelPairing.cs</c> says so in its own remarks.
/// </para>
/// <para>
/// <b>Why this is a database test and not an application test.</b> The importer checks the pairing
/// when it reads a PDF, which is the application keeping the database's promise. This asserts that
/// the database keeps it too - so a row written by a script, a hand-written psql session, a
/// backfill, or a future code path that forgets the check is refused rather than accepted.
/// </para>
/// <para>
/// <b>The instrument is a foreign key, beside the trigger that is still there.</b> The key is the
/// pair <c>(fuel_type_id, brand_id)</c> pointing at the fuel's own <c>(id, brand_id)</c>, so the row's
/// brand and its fuel's brand are one fact rather than two that a function compares. The key is
/// enforced by the engine, it cannot fall out of step with the schema, and it costs one index probe
/// rather than a query per write.
/// </para>
/// <para>
/// <b>The trigger is still there, and it guards the other column.</b> The trigger asks the same
/// question about the <b>name</b>, and the key asks it about the <b>id</b> - so a row whose name says
/// one brand and whose key says another is refused by neither. The trigger goes with the name, in
/// the step that moves the readers to the id.
/// </para>
/// <para>
/// The seeded catalogue is what the test leans on: <c>okko-95</c> is an OKKO fuel and <c>wog-95</c> is
/// a WOG one, so the test states a brand that is wrong by exactly one letter and nothing else.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class BrandMatchesItsFuelTypeIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public BrandMatchesItsFuelTypeIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task A_Voucher_Whose_Brand_Is_Not_Its_Fuel_Type_S_Brand_Is_Refused()
    {
        await using var context = CreateContext();

        var voucher = new FuelVoucher
        {
            Id = Guid.NewGuid(),
            // WOG, on a fuel that belongs to OKKO.
            BrandId = BrandIds.ForSlug("wog"),
            Brand = "wog",
            FuelTypeId = ToUuid("okko-95"),
            Liters = 50m,
            SupplierExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
            CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
            VoucherNumber = $"WRONG-BRAND-{Guid.NewGuid():N}",
            QrPayload = $"payload-{Guid.NewGuid():N}",
            Status = VoucherStatus.Available,
            DataKind = DataKinds.Real,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        context.FuelVouchers.Add(voucher);

        var act = async () => await context.SaveChangesAsync();

        // The refusal is the database's, so it arrives wrapped: EF's own message is the generic
        // "an error occurred while saving", and the one that says what was wrong is inside it.
        var thrown = (await act.Should().ThrowAsync<DbUpdateException>()).Which;

        var inner = thrown.InnerException;
        var message = (inner?.Message ?? thrown.Message);
        message.Should().Contain("is not the brand",
            "the refusal says which brand was wrong and which one was right");
    }

    [Fact]
    public async Task A_Voucher_Whose_Brand_Key_Is_Not_Its_Fuel_Type_S_Brand_Is_Refused()
    {
        // The case the key catches and the trigger cannot see: the NAME is right and the KEY is
        // wrong. The trigger compares the name with the fuel's brand, so it passes this row; the key
        // compares the pair, and the pair is not a row of the catalogue.
        //
        // This is the guarantee the key adds, and it is the reason the key is not merely a cheaper
        // trigger: before it, a writer that mixed the two copies up wrote a row that nothing
        // refused.
        await using var context = CreateContext();

        var voucher = new FuelVoucher
        {
            Id = Guid.NewGuid(),
            // The name is right - OKKO, on an OKKO fuel.
            Brand = "okko",
            // The key is WOG's. The row says OKKO and carries WOG's key, and the fuel is OKKO's.
            BrandId = BrandIds.ForSlug("wog"),
            FuelTypeId = ToUuid("okko-95"),
            Liters = 50m,
            SupplierExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
            CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
            VoucherNumber = $"WRONG-KEY-{Guid.NewGuid():N}",
            QrPayload = $"payload-{Guid.NewGuid():N}",
            Status = VoucherStatus.Available,
            DataKind = DataKinds.Real,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        context.FuelVouchers.Add(voucher);

        var act = async () => await context.SaveChangesAsync();

        // The refusal is the database's, so it arrives wrapped: EF's own message is the generic
        // "an error occurred while saving", and the one that says what was wrong is inside it.
        var thrown = (await act.Should().ThrowAsync<DbUpdateException>()).Which;

        var inner = thrown.InnerException;
        var message = (inner?.Message ?? thrown.Message);
        message.Should().Contain("FK_fuel_vouchers_fuel_types_fuel_type_id_brand_id",
            "the refusal names the key that was violated - the pair, because the pair is what says the brand");
    }

    [Fact]
    public async Task A_Voucher_Whose_Brand_Is_Its_Fuel_Type_S_Brand_Is_Accepted()
    {
        await using var context = CreateContext();

        var voucher = new FuelVoucher
        {
            Id = Guid.NewGuid(),
            BrandId = BrandIds.ForSlug("okko"),
            Brand = "okko",
            FuelTypeId = ToUuid("okko-95"),
            Liters = 50m,
            SupplierExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
            CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
            VoucherNumber = $"RIGHT-BRAND-{Guid.NewGuid():N}",
            QrPayload = $"payload-{Guid.NewGuid():N}",
            Status = VoucherStatus.Available,
            DataKind = DataKinds.Real,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        context.FuelVouchers.Add(voucher);

        var act = async () => await context.SaveChangesAsync();

        await act.Should().NotThrowAsync("the brand that agrees with the fuel is the ordinary case");
    }

    [Fact]
    public async Task An_Order_Line_Whose_Brand_Is_Not_Its_Fuel_Type_S_Brand_Is_Refused()
    {
        await using var context = CreateContext();

        var line = new OrderLineItem
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            // KLO, on a fuel that belongs to OKKO.
            BrandId = BrandIds.ForSlug("klo"),
            Brand = "klo",
            FuelTypeId = ToUuid("okko-95"),
            Liters = 50m,
            Quantity = 1,
            UnitPrice = 2500,
            LineTotal = 2500
        };

        context.OrderLineItems.Add(line);

        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task The_Key_Is_A_Foreign_Key_And_The_Trigger_Still_Guards_The_Name()
    {
        // Two instruments, two columns, and this is the part that is easy to get wrong: the key
        // checks the ID, the trigger checks the NAME, and neither sees what the other sees. A row
        // whose name says OKKO and whose key says WOG is refused by neither - so the trigger stays
        // while the name is still a column, and it goes with the name.
        //
        // The importer is why the two copies are not redundant: the name is what the supplier's
        // paper says and the key is what the catalogue says, so a misread paper writes a wrong name
        // with a right key, and the trigger is the only thing that sees it.
        await using var context = CreateContext();

        var keys = await context.Database
            .SqlQueryRaw<string>(
                """
                SELECT con.conname AS "Value"
                FROM pg_constraint con
                JOIN pg_class rel ON rel.oid = con.conrelid
                WHERE rel.relname IN ('fuel_vouchers', 'order_line_items', 'purchase_batches')
                  AND con.contype = 'f'
                  AND con.conname LIKE '%fuel_type_id_brand_id'
                """)
            .ToListAsync();

        keys.Should().HaveCount(3, "every table that carries a brand is checked by the pair");

        var triggers = await context.Database
            .SqlQueryRaw<string>(
                """
                SELECT tg.tgname AS "Value"
                FROM pg_trigger tg
                JOIN pg_class rel ON rel.oid = tg.tgrelid
                WHERE rel.relname IN ('fuel_vouchers', 'order_line_items', 'purchase_batches')
                  AND NOT tg.tgisinternal
                  AND tg.tgname LIKE '%brand%'
                """)
            .ToListAsync();

        triggers.Should().HaveCount(3,
            "the trigger still guards the name - it goes with the name, in the step that moves the readers to the id");
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);
}

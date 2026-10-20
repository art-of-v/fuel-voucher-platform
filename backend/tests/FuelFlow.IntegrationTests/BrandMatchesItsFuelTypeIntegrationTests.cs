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
/// The brand on a row and the brand of the fuel it claims are the same fact, and the database says
/// so.
/// </summary>
/// <remarks>
/// <para>
/// Four tables carry a brand as a name - <c>fuel_vouchers</c>, <c>order_line_items</c>,
/// <c>purchase_batches</c>, <c>voucher_exchanges</c> - and each of them also names the fuel the row
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
    public async Task A_Voucher_Whose_Brand_Is_Its_Fuel_Type_S_Brand_Is_Accepted()
    {
        await using var context = CreateContext();

        var voucher = new FuelVoucher
        {
            Id = Guid.NewGuid(),
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

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);
}

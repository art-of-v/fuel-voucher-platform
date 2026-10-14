using System.Text.Json;
using FluentAssertions;
using FuelFlow.SharedKernel.DTOs;
using Xunit;

namespace FuelFlow.UnitTests.SharedKernel;

/// <summary>
/// Pins the transitional wire alias that lets the brand rename merge independently of the App Store.
/// </summary>
/// <remarks>
/// <para>
/// The published 1.0.1 app reads <c>provider</c> off five response shapes and cannot be made to read
/// <c>brand</c>: that bundle is already in the App Store, and only a new build changes what a client
/// reads. Renaming the field outright left every current user's wallet with no brand - no network
/// colour on the cards, and a filter bar whose provider list is built from an empty set - so the
/// rename had to wait for 1.1.0. Shipping both names for one release removes that coupling: the
/// rename merges whenever, and the old name goes afterwards.
/// </para>
/// <para>
/// The failure nobody would notice is the one this guards. Nothing breaks when the alias is removed
/// too early - the new app keeps working, because it reads <c>brand</c>. What breaks is the fleet on
/// 1.0.1, and it breaks silently: no error, no failed request, just an unbranded wallet. So the
/// removal condition lives in a test rather than in anyone's memory.
/// </para>
/// <para>
/// <b>How to read a failure.</b> If 1.1.0 is live and the fleet has had time to take the OTA, remove
/// the five <c>Provider</c> properties and delete this file - the shapes then have no alias and there
/// is nothing left to assert. OTA applies on the <i>second</i> launch, so "released" is not the same as
/// "in use"; wait for the fleet to drain. If the rename is still blocked on App Review, nothing is
/// wrong - the alias is doing the work, and deleting it as tidying is exactly the mistake.
/// </para>
/// </remarks>
public class TransitionalBrandAliasTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, Web);

    private static string? Field<T>(T value, string name)
    {
        using var document = JsonDocument.Parse(Json(value));
        return document.RootElement.TryGetProperty(name, out var field) ? field.GetString() : null;
    }

    /// <summary>
    /// The shapes the published 1.0.1 bundle reads <c>provider</c> from. Admin-facing DTOs are
    /// deliberately absent: the admin panel deploys with the backend and is never a version behind.
    /// </summary>
    public static TheoryData<Type> MobileFacingShapes() => new()
    {
        typeof(VoucherDto),
        typeof(OrderLineItemDto),
        typeof(PurchaseDto),
        typeof(FuelFlow.Features.Vouchers.GetUserVouchers.VoucherDto),
        typeof(FuelFlow.Features.Vouchers.Renewal.Quote.RenewalVoucherQuote),
        typeof(FuelFlow.Features.Vouchers.GetWorkerUsageReport.WorkerUsageListItem),
    };

    /// <summary>
    /// The alias must exist on every mobile-facing shape, be a view rather than a second field, and
    /// carry the same value as <c>brand</c>. A settable duplicate would be a second source of truth
    /// that could disagree - the card would paint one network's colour while the filter bar matched
    /// another.
    /// </summary>
    [Theory]
    [MemberData(nameof(MobileFacingShapes))]
    public void EveryMobileFacingShapeCarriesBothNames(Type shape)
    {
        var alias = shape.GetProperty("Provider");

        alias.Should().NotBeNull(
            $"{shape.Name} lost its transitional `provider` alias. If 1.1.0 is live and the fleet has "
            + "drained, that is deliberate - delete this test too. If not, deleting it now strips the "
            + "brand from every 1.0.1 wallet, silently");

        alias!.SetMethod.Should().BeNull("a settable `provider` could drift from `brand`");
        alias.PropertyType.Should().Be(typeof(string));
    }

    [Fact]
    public void VoucherDto_SendsBothNamesWithTheSameValue()
    {
        var dto = new VoucherDto { Brand = "OKKO", FuelType = "okko-95", FuelName = "A-95" };

        Field(dto, "provider").Should().Be("OKKO");
        Field(dto, "brand").Should().Be("OKKO");
    }

    [Fact]
    public void OrderLineItemDto_SendsBothNamesWithTheSameValue()
    {
        var dto = new OrderLineItemDto { Brand = "WOG", FuelTypeId = "wog-95" };

        Field(dto, "provider").Should().Be("WOG");
        Field(dto, "brand").Should().Be("WOG");
    }

    [Fact]
    public void PurchaseDto_SendsBothNamesWithTheSameValue()
    {
        var dto = new PurchaseDto { Brand = "KLO", ProductType = "Fuel" };

        Field(dto, "provider").Should().Be("KLO");
        Field(dto, "brand").Should().Be("KLO");
    }

    [Fact]
    public void MyVouchers_SendsBothNamesWithTheSameValue()
    {
        var dto = new FuelFlow.Features.Vouchers.GetUserVouchers.VoucherDto(
            Guid.NewGuid(), "UPG", "upg-95", 10m, 999m,
            DateOnly.FromDateTime(DateTime.UtcNow), "12345", "ext", "qr", "qrcode",
            "Assigned", "Purchase", null, null, null, null, null, null, null,
            DateTime.UtcNow, DateTime.UtcNow);

        Field(dto, "provider").Should().Be("UPG");
        Field(dto, "brand").Should().Be("UPG");
    }

    [Fact]
    public void RenewalQuote_SendsBothNamesWithTheSameValue()
    {
        var quote = new FuelFlow.Features.Vouchers.Renewal.Quote.RenewalVoucherQuote { Brand = "Shell" };

        Field(quote, "provider").Should().Be("Shell");
        Field(quote, "brand").Should().Be("Shell");
    }

    [Fact]
    public void WorkerUsageItem_SendsBothNamesWithTheSameValue()
    {
        var item = new FuelFlow.Features.Vouchers.GetWorkerUsageReport.WorkerUsageListItem(
            Guid.NewGuid(), "Socar", "socar-95", "999", 10m, "Assigned",
            DateOnly.FromDateTime(DateTime.UtcNow), null, null, false);

        Field(item, "provider").Should().Be("Socar");
        Field(item, "brand").Should().Be("Socar");
    }
}
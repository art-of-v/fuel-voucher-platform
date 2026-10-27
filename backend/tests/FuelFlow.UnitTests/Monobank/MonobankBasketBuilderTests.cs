using FluentAssertions;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;

namespace FuelFlow.UnitTests.Monobank;

/// <summary>
/// What the platform declares to the acquirer about a sale, before it becomes a fiscal receipt.
/// </summary>
/// <remarks>
/// <para>
/// Two things are pinned here, and the second one is a regression. The line sells the advertising
/// service the QR code is bought under, not the fuel the station sells. And the arithmetic has to
/// hold: <see cref="MonobankBasketItem.Total"/> is defined as <c>Sum × Qty</c>, while
/// <c>UnitPrice</c> on an order line is the price of ONE code — a 10 L code is one unit at 862.00,
/// not ten units at 86.20. Counting litres against that price made every basket line declare a
/// unit price ten times what it charged, which is the disagreement an inspector reads as a
/// mis-declared receipt.
/// </para>
/// </remarks>
public sealed class MonobankBasketBuilderTests
{
    private static readonly Guid FuelTypeId = Guid.Parse("3808d410-1f2e-4a5b-8c9d-0e1f2a3b4c5d");

    private static MonobankBasketItem OneLine(decimal liters, int quantity, decimal unitPrice)
    {
        var items = MonobankBasketBuilder.Build(
            [(FuelTypeId, liters, quantity, unitPrice, unitPrice * quantity)]);

        items.Should().ContainSingle();
        return items[0];
    }

    [Fact]
    public void A_line_sells_the_advertising_service_not_the_fuel()
    {
        var item = OneLine(10m, 1, 862.00m);

        item.Name.Should().Be("Рекламні послуги");
        item.Unit.Should().Be("послуга");

        // The fuel is not the sale, so it is not the name; it stays as the line's code, which is
        // what lets a refund point back at the same thing.
        item.Code.Should().Be(FuelTypeId.ToString());
    }

    // A decimal literal is not a legal attribute argument (the metadata has no encoding for it),
    // so the cases carry doubles and convert on the way in.
    [Theory]
    [InlineData(10, 1, 862.00)]
    [InlineData(10, 2, 862.00)]
    [InlineData(1, 3, 91.00)]
    [InlineData(40, 1, 3200.50)]
    public void The_declared_total_is_the_declared_unit_price_times_the_declared_quantity(
        int liters, int quantity, double unitPrice)
    {
        var item = OneLine(liters, quantity, (decimal)unitPrice);

        item.Qty.Should().Be(quantity, "codes are counted, not litres");
        item.Sum.Should().Be((long)((decimal)unitPrice * 100), "the price of ONE code");
        ((decimal)item.Total).Should().Be(item.Sum * item.Qty);
    }

    [Theory]
    [InlineData(10, 0)]
    [InlineData(0, 1)]
    public void A_line_that_describes_no_code_declares_nothing(int liters, int quantity)
    {
        MonobankBasketBuilder.Build([(FuelTypeId, liters, quantity, 862.00m, 862.00m * quantity)])
            .Should().BeEmpty();
    }
}

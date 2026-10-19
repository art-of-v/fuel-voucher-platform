using FuelFlow.Features.Pricing.ScrapeMinfinPrices;
using FluentAssertions;
using Xunit;

namespace FuelFlow.UnitTests.Pricing;

/// <summary>
/// Minfin's fuel price table, read the way it is written.
/// </summary>
/// <remarks>
/// The fixture is the table's own markup, with the parts that matter: the caption that dates it,
/// the five grade columns, and a row per network with the prices written with a comma as the
/// decimal separator.
/// </remarks>
public sealed class MinfinPricePageTests
{
    private const string Table = """
        <table border=0 cellspacing=0 cellpadding=3 class='zebra'>
        <caption>Середні ціни за провідними операторами на&nbsp;19.10.2026 <span class='normal nowrap'>(грн./літр)</span></caption>
        <tr><th colspan=2 align='left' class='blank'><span class='sortspan '>Оператор</span></th><th align='right' class='blank'>А&nbsp;95+</a></th><th align='right' class='blank'>А&nbsp;95</a></th><th align='right' class='blank'>А&nbsp;92</a></th><th align='right' class='blank'>ДП</a></th><th align='right' class='blank'>Газ</a></th></tr>
        <tr><td align='left' class='r0'><a href='/ua/markets/fuel/tm/am ic/'>AMIC</a></td><td align='center' class='r0'><br></td><td align='right' class='r0'>91,96</td><td align='right' class='r0'>88,96</td><td align='right' class='r0'><br></td><td align='right' class='r0'>101,18</td><td align='right' class='r0'>50,79</td></tr>
        <tr><td align='left' class='r1'><a href='/ua/markets/fuel/tm/klo/'>KLO</a></td><td align='center' class='r1'><br></td><td align='right' class='r1'>95,60</td><td align='right' class='r1'>87,90</td><td align='right' class='r1'>86,50</td><td align='right' class='r1'>101,90</td><td align='right' class='r1'>50,90</td></tr>
        <tr><td align='left' class='r0'><a href='/ua/markets/fuel/tm/wog/'>WOG</a></td><td align='center' class='r0'><br></td><td align='right' class='r0'>92,90</td><td align='right' class='r0'>89,90</td><td align='right' class='r0'><br></td><td align='right' class='r0'>103,00</td><td align='right' class='r0'>48,57</td></tr>
        </table>
        """;

    [Fact]
    public void The_row_of_one_network_is_read()
    {
        var prices = MinfinPricePage.Parse(Table, "klo");

        prices.Should().HaveCount(5);
        prices.Select(p => p.Grade).Should().BeEquivalentTo(["А 95+", "А 95", "А 92", "ДП", "Газ"]);
    }

    [Fact]
    public void The_prices_are_read_with_a_comma_as_the_decimal_separator()
    {
        var prices = MinfinPricePage.Parse(Table, "klo");

        // 95,60 read as ninety-five would be a catalogue a hundred times too dear, and every
        // downstream guard still passes, because a higher price is never "below cost".
        prices.Single(p => p.Grade == "А 95+").PricePerLiter.Should().Be(95.60m);
        prices.Single(p => p.Grade == "А 95").PricePerLiter.Should().Be(87.90m);
        prices.Single(p => p.Grade == "ДП").PricePerLiter.Should().Be(101.90m);
        prices.Single(p => p.Grade == "Газ").PricePerLiter.Should().Be(50.90m);
    }

    [Fact]
    public void A_network_that_does_not_sell_a_grade_leaves_the_cell_empty()
    {
        var prices = MinfinPricePage.Parse(Table, "wog");

        // AMIC and WOG do not sell A-92, and the cell is empty rather than zero. A zero would be a
        // price, and a price of zero is a free grade.
        prices.Should().NotContain(p => p.Grade == "А 92");
        prices.Single(p => p.Grade == "А 95").PricePerLiter.Should().Be(89.90m);
    }

    [Fact]
    public void A_network_that_is_not_in_the_table_is_refused()
    {
        var act = () => MinfinPricePage.Parse(Table, "klo-does-not-exist");

        act.Should().Throw<MinfinPriceScrapeException>(
            "a network missing from the table is a page that changed, not a network that charges nothing");
    }

    [Fact]
    public void The_day_the_table_is_dated_is_read()
    {
        // A price from last Tuesday is a price, but it is last Tuesday's.
        MinfinPricePage.DateOf(Table).Should().Be("19.10.2026");
    }
}

using System.Text.Json;
using FuelFlow.Features.Pricing.ScrapeUpgWogPrices;
using FluentAssertions;
using Xunit;

namespace FuelFlow.UnitTests.Pricing;

/// <summary>
/// The two page shapes UPG and WOG publish, read the way the vendor's page is actually written.
/// </summary>
/// <remarks>
/// The fixtures are the real ones: UPG's blob is the page's own JavaScript, and WOG's is the
/// Next.js state the CI's node test reads
/// (<c>scripts/fixtures/wog-next-page-state.json</c>). A parser that only ever saw a hand-written
/// sample would agree with a page that has changed shape, which is the one thing a scraper must
/// not do.
/// </remarks>
public sealed class UpgWogPricePageTests
{
    /// <summary>
    /// UPG's shape, taken from the page: a JavaScript assignment holding a JSON array of grades.
    /// The prices carry float noise, because the vendor's arithmetic does.
    /// </summary>
    private const string UpgBlobPage = """
        <html><body><script>
        let averagePrices = {"data":[{"Id":4,"Title":"Газ","Price":49.899999999999999},{"Id":9,"Title":"upg95","Price":91.900000000000006},{"Id":1,"Title":"A-95","Price":88.90000000000001}]};
        </script></body></html>
        """;

    /// <summary>
    /// WOG's shape: the Next.js state. The grade is split across two fields and the price is in
    /// kopecks, which is the combination that makes a naive reader a hundred times too dear.
    /// </summary>
    private const string WogNextPage = """
        <html><body><script id="__NEXT_DATA__" type="application/json">
        {"props":{"pageProps":{"initialReduxState":{"pageReducer":{"elements":[{"block":{"items":[
        {"price":10350,"i_am_page_element":"block.fuels_fuel","type":"ДП","brand":"Євро5-Е10"},
        {"price":9250,"i_am_page_element":"block.fuels_fuel","type":"95","brand":"Mustang"}
        ]}}]}}}}}
        </script></body></html>
        """;

    [Fact]
    public void The_blob_is_found_by_its_assignment_not_by_a_path()
    {
        var prices = UpgWogPricePage.Parse(UpgBlobPage);

        prices.Should().HaveCount(3);
        prices.Select(p => p.FuelCode).Should().BeEquivalentTo(["Газ", "upg95", "A-95"]);
    }

    [Fact]
    public void The_blob_price_is_rounded_rather_than_written_with_its_float_noise()
    {
        var prices = UpgWogPricePage.Parse(UpgBlobPage);

        // 49.899999999999999 is the vendor's arithmetic, not a price. Written raw it is a number
        // every downstream reader has to know to round.
        prices.Single(p => p.FuelCode == "Газ").PricePerLiter.Should().Be(49.90m);
        prices.Single(p => p.FuelCode == "upg95").PricePerLiter.Should().Be(91.90m);
    }

    [Fact]
    public void The_next_page_state_is_found_by_its_tag()
    {
        var prices = UpgWogPricePage.Parse(WogNextPage);

        prices.Should().HaveCount(2);
    }

    [Fact]
    public void The_next_page_grade_is_its_type_and_brand_joined()
    {
        var prices = UpgWogPricePage.Parse(WogNextPage);

        prices.Select(p => p.FuelCode).Should().BeEquivalentTo(["ДП Євро5-Е10", "95 Mustang"]);
    }

    [Fact]
    public void The_next_page_price_is_kopecks()
    {
        var prices = UpgWogPricePage.Parse(WogNextPage);

        // 10350 kopecks is 103.50 UAH. Read as UAH the catalogue is a hundred times too dear, and
        // every downstream guard still passes, because a higher price is never "below cost".
        prices.Single(p => p.FuelCode == "ДП Євро5-Е10").PricePerLiter.Should().Be(103.50m);
        prices.Single(p => p.FuelCode == "95 Mustang").PricePerLiter.Should().Be(92.50m);
    }

    [Fact]
    public void A_page_with_neither_shape_is_refused()
    {
        var act = () => UpgWogPricePage.Parse("<html><body>nothing here</body></html>");

        act.Should().Throw<UpgWogPriceScrapeException>(
            "a page that cannot be read must leave the catalogue alone rather than write half of one");
    }

    [Fact]
    public void A_page_whose_shape_carries_no_price_is_refused()
    {
        var act = () => UpgWogPricePage.Parse(
            "<html><body><script>let averagePrices = {\"data\":[{\"Title\":\"upg95\",}]};</script></body></html>");

        act.Should().Throw<UpgWogPriceScrapeException>();
    }

    [Fact]
    public void A_repeated_grade_is_kept_once()
    {
        var page = """
            <html><body><script>
            let averagePrices = {"data":[{"Title":"upg95","Price":91.9},{"Title":"upg95","Price":95.0}]};
            </script></body></html>
            """;

        var prices = UpgWogPricePage.Parse(page);

        prices.Should().HaveCount(1, "the page repeating a component is not a second grade");
        prices[0].PricePerLiter.Should().Be(91.90m);
    }

    [Fact]
    public void The_sheet_is_the_two_columns_the_importer_reads()
    {
        var sheet = UpgWogPriceSheet.Write([new UpgWogPrice("upg95", 91.90m)]);

        sheet.Should().StartWith("fuelCode,pricePerLiter\r\n");
        sheet.Should().Contain("upg95,91.90");
    }

    [Fact]
    public void A_code_with_a_comma_is_quoted()
    {
        var sheet = UpgWogPriceSheet.Write([new UpgWogPrice("ДП, 5", 10m)]);

        sheet.Should().Contain("\"ДП, 5\",10.00");
    }
}
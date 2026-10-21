using FluentAssertions;
using FuelFlow.Features.Brands;
using FuelFlow.Features.ErrorLogs.Logging;
using FuelFlow.Features.Orders.CreateCheckout;
using FuelFlow.Features.Pricing.Terms.SharedModels;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Vouchers.Terms;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// A fuel the catalogue publishes but nobody has costed, met by the term quote.
/// </summary>
/// <remarks>
/// <para>
/// The OKKO catalogue import writes a fuel's pump price and leaves the supplier cost and the margin
/// empty, because nobody has costed that grade yet. The storefront copes with that state - it shows
/// the fuel and marks it unavailable - and so must the term quote, which is the other surface that
/// prices a line for the customer.
/// </para>
/// <para>
/// It did not. The quote asked the throwing price function for a number, got an exception, and the
/// global handler turned it into a 400 with a generic title - and, because the exception was logged
/// at Error, a row in the admin error journal for every card on the shelf. Measured on production
/// 2026-10-19: 24 rows in seventeen seconds, all of them the same business state. The customer saw a
/// shelf without a term picker; the operator saw a journal full of rows that said nothing.
/// </para>
/// <para>
/// Both halves are pinned here against a real database: the quote answers with the ladder and no
/// prices, and the refusal leaves no row in the journal.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class UnpricedTermQuoteIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public UnpricedTermQuoteIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    [Fact]
    public async Task The_Term_Quote_Answers_For_A_Fuel_Nobody_Has_Priced()
    {
        var fuelId = $"unpriced-term-quote-{Guid.CreateVersion7()}";

        await using (var seed = CreateContext())
        {
            seed.FuelTypes.Add(new FuelTypeEntity
            {
                Id =   ToUuid(fuelId),
                Name = "Unpriced term-quote fuel",
                BrandId = BrandIds.Okko,
                BasePrice = 0m,
                DiscountPrice = null,
                // What the catalogue import leaves behind: the pump is published, nobody has set a
                // cost or a margin, so there is no price to quote.
                PumpPricePerLiter = 92.90m,
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            });

            seed.FuelNominals.Add(new FuelNominal
            {
                FuelTypeId =   ToUuid(fuelId),
                Liters = 10m,
                DataKind = DataKinds.Real,
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            });

            // A ladder, so the quote has something to say - and cannot say. The migration seeds one
            // open row per term and scheme, so the tier is priced by updating that row rather than
            // by inserting a second one the unique index would refuse.
            var tier = await seed.VoucherTermPricings.SingleAsync(t =>
                t.Scheme == VoucherTermPricingSchemes.PurchaseTermDiscount && t.TermCode == "1w");
            tier.Enabled = true;
            tier.RatePerLiter = 20m;

            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext();
        var handler = new TermQuoteQueryHandler(
            context,
            new RuntimeSettingsService(context),
            new BrandSlugResolver(context));

        var quote = await handler.HandleAsync(isTestAccount: false, BrandIds.Okko, ToUuid(fuelId), 10m);

        quote.Terms.Should().OnlyContain(t => t.PricePerLiterUah == null, "there is no price to show");
        quote.Terms.Should().OnlyContain(t => t.LinePriceUah == 0m, "and no line price to charge");
        quote.Terms.Should().OnlyContain(t => !t.Available, "a fuel with no price cannot be bought on any term");
    }

    [Fact]
    public async Task A_Refusal_For_An_Unpriced_Fuel_Leaves_No_Row_In_The_Error_Journal()
    {
        // The buy path still refuses such a fuel - charging nothing is not an option - and that
        // refusal is a business state, not an incident. The journal keeps Error and Critical records
        // only, and the noise filter drops this one, so a customer who keeps trying does not bury
        // the failures that are worth reading.
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"error_logs\" RESTART IDENTITY CASCADE");

        await using (var dataSource = new NpgsqlDataSourceBuilder(_fixture.ConnectionString).Build())
        {
            var services = new ServiceCollection().AddSingleton(dataSource).BuildServiceProvider();
            using var brand = new DatabaseLoggerProvider(services);

            brand.CreateLogger("FuelFlow.Middleware.GlobalExceptionHandler").LogError(
                new NoPriceAvailableException("The fuel has no supplier cost and no margin, so there is no price to charge."),
                "Unhandled exception for {Method} {Path}", "POST", "/api/purchases");
        }

        var rows = await context.ErrorLogs.AsNoTracking().CountAsync();
        rows.Should().Be(0, "a catalogue state that repeats itself is not an incident");
    }
}

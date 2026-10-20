using FluentAssertions;
using FuelFlow.Features.Pricing.Terms.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The price windows of one tier may not overlap.
/// </summary>
/// <remarks>
/// The table carried two protections and neither of them said what "no overlap" means: a partial
/// unique index pinned ONE OPEN row per (scheme, term_code), and a CHECK pinned that an end is
/// after a start. Two <b>closed</b> windows for the same tier still satisfied both - so the
/// lookup was left with two candidate rows for the same date, and the answer depended on which one
/// the query happened to pick.
///
/// The exclusion constraint is the instrument that says what the question means. These tests pin
/// that it is there, that it refuses an overlap, and that it does NOT refuse the history the
/// write path produces - a window that ends exactly where the next one begins.
/// </remarks>
[Collection("Integration Tests")]
public sealed class TermPricingOverlapTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public TermPricingOverlapTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    [Fact]
    public async Task The_Exclusion_Constraint_Is_Installed()
    {
        await using var context = CreateContext();

        var constraints = await context.Database.SqlQueryRaw<string>(
            """
            SELECT con.conname AS "Value"
            FROM pg_constraint con
            JOIN pg_class rel ON rel.oid = con.conrelid
            WHERE rel.relname = 'voucher_term_pricing'
              AND con.contype = 'x'
            """).ToListAsync();

        constraints.Should().ContainSingle(
            "a name is only worth having if the database pins the rule");
    }

    [Fact]
    public async Task An_Overlapping_Window_Is_Refused()
    {
        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var scheme = VoucherTermPricingSchemes.RenewalRate;

        await using (var context = CreateContext())
        {
            await ClearSchemeAsync(context, scheme);
            context.VoucherTermPricings.Add(Window(scheme, "1m", from, to));
            await context.SaveChangesAsync();
        }

        await using var overlapping = CreateContext();
        overlapping.VoucherTermPricings.Add(Window(
            scheme,
            "1m",
            new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)));

        var act = async () => await overlapping.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>(
            "two closed windows that overlap leave the lookup with two candidate rows for the same date");
    }

    [Fact]
    public async Task A_Window_That_Ends_Where_The_Next_Begins_Is_Allowed()
    {
        // This is the shape the write path produces: the open row is closed and the new one opens
        // at the same instant. The windows touch, and a half-open range does not overlap.
        var boundary = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var scheme = VoucherTermPricingSchemes.RenewalRate;

        await using var context = CreateContext();

        await ClearSchemeAsync(context, scheme);

        context.VoucherTermPricings.Add(Window(
            scheme,
            "1w",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            boundary));
        context.VoucherTermPricings.Add(Window(
            scheme,
            "1w",
            boundary,
            null));

        var act = async () => await context.SaveChangesAsync();

        await act.Should().NotThrowAsync(
            "the price history is a chain of windows that meet end to end");
    }

    [Fact]
    public async Task A_Different_Tier_May_Overlap()
    {
        // The constraint is per (scheme, term_code). Two different tiers are two different ladders.
        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        await using var context = CreateContext();

        await ClearSchemeAsync(context, VoucherTermPricingSchemes.RenewalRate);
        await ClearSchemeAsync(context, VoucherTermPricingSchemes.PurchaseTermDiscount);

        context.VoucherTermPricings.Add(Window(VoucherTermPricingSchemes.RenewalRate, "2m", from, to));
        context.VoucherTermPricings.Add(Window(VoucherTermPricingSchemes.RenewalRate, "1m", from, to));
        context.VoucherTermPricings.Add(Window(VoucherTermPricingSchemes.PurchaseTermDiscount, "2m", from, to));

        var act = async () => await context.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// The seed installs an open price window for every tier. This test is about what the
    /// constraint allows, so it starts from empty ladders - otherwise the seeded windows are part
    /// of the answer.
    /// </summary>
    private static async Task ClearSchemeAsync(ApplicationDbContext context, string scheme)
        => await context.VoucherTermPricings
            .Where(r => r.Scheme == scheme)
            .ExecuteDeleteAsync();

    private static VoucherTermPricing Window(string scheme, string termCode, DateTime from, DateTime? to) => new()
    {
        Id = Guid.NewGuid(),
        Scheme = scheme,
        TermCode = termCode,
        Enabled = true,
        RatePerLiter = 10m,
        ValidFrom = from,
        ValidTo = to,
        CreatedAtUtc = DateTime.UtcNow
    };
}

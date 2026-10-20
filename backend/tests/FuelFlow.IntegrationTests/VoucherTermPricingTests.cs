using FluentAssertions;
using FuelFlow.Features.Pricing.Terms.SharedModels;
using FuelFlow.Features.Vouchers.Renewal;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// <c>voucher_term_pricing</c> — the term ladders that were 32 unversioned
/// <c>app_settings</c> strings.
/// </summary>
/// <remarks>
/// <para>
/// The database half of the guarantee lives here rather than in a unit test, because the load-bearing
/// part is an index: <c>ux_voucher_term_pricing_open_tier</c> is partial on <c>valid_to IS NULL</c>, so
/// it refuses two <i>current</i> prices for one tier while leaving closed history free. Nothing in C#
/// has to be trusted to say so.
/// </para>
/// <para>
/// The CHECKs matter for the same reason the fuel-code index does: a psql session writing a tier does
/// not go through the handler, and a rate of <c>-5</c> is a negative price for a renewal or a payment
/// to the customer for a term discount.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class VoucherTermPricingTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public VoucherTermPricingTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    private VoucherTermPricing Tier(string scheme, string termCode, bool enabled, decimal rate, DateTime? validTo = null)
        => new()
        {
            Id = Guid.NewGuid(),
            Scheme = scheme,
            TermCode = termCode,
            Enabled = enabled,
            RatePerLiter = rate,
            ValidFrom = DateTime.UtcNow.AddDays(-1),
            ValidTo = validTo,
            CreatedAtUtc = DateTime.UtcNow
        };

    private static async Task CleanAsync(ApplicationDbContext db)
        => await db.Database.ExecuteSqlRawAsync("DELETE FROM voucher_term_pricing");

    [Fact]
    public async Task TwoOpenPricesForOneTier_AreRefused()
    {
        await using var db = CreateContext();
        await CleanAsync(db);

        db.VoucherTermPricings.Add(Tier(VoucherTermPricingSchemes.RenewalRate, "1w", true, 24.5m));
        db.VoucherTermPricings.Add(Tier(VoucherTermPricingSchemes.RenewalRate, "1w", true, 26m));

        // The index, not the C# - nothing above tried to prevent this, and the first save is where
        // Postgres refuses.
        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();

        await CleanAsync(db);
    }

    [Fact]
    public async Task AClosedPriceAndAnOpenOne_Coexist_BecauseTheHistoryIsThePoint()
    {
        await using var db = CreateContext();
        await CleanAsync(db);

        // The closed window ends exactly where the open one begins. That is the shape the write
        // path produces, and it is what "history" means here: the price was X until the moment
        // it became Y, with no instant in between belonging to neither.
        //
        // The two windows must NOT overlap. A closed window that reaches into the open one leaves
        // the lookup with two candidates for the same date - which is what the exclusion
        // constraint refuses, and what this test's neighbour refuses.
        var boundary = DateTime.UtcNow.AddHours(-1);

        var past = Tier(VoucherTermPricingSchemes.RenewalRate, "2w", true, 24.5m, validTo: boundary);
        past.ValidFrom = boundary.AddDays(-1);
        db.VoucherTermPricings.Add(past);

        var open = Tier(VoucherTermPricingSchemes.RenewalRate, "2w", true, 26m);
        open.ValidFrom = boundary;
        db.VoucherTermPricings.Add(open);

        await db.SaveChangesAsync();

        var rows = await db.VoucherTermPricings
            .Where(r => r.Scheme == VoucherTermPricingSchemes.RenewalRate && r.TermCode == "2w")
            .ToListAsync();

        rows.Should().HaveCount(2);
        rows.Count(r => r.ValidTo == null).Should().Be(1);

        await CleanAsync(db);
    }

    [Fact]
    public async Task TheSameTermCodeInTwoSchemes_IsNotADuplicate()
    {
        await using var db = CreateContext();
        await CleanAsync(db);

        // "1w" is a renewal price in one row and a term discount in the other. Forcing them into one
        // meaning is exactly what the scheme discriminator exists to prevent.
        db.VoucherTermPricings.Add(Tier(VoucherTermPricingSchemes.RenewalRate, "1w", true, 24.5m));
        db.VoucherTermPricings.Add(Tier(VoucherTermPricingSchemes.PurchaseTermDiscount, "1w", true, 3m));
        await db.SaveChangesAsync();

        var open = await db.VoucherTermPricings.Where(r => r.ValidTo == null).ToListAsync();
        open.Should().HaveCount(2);
        open.Select(r => r.Scheme).Should().BeEquivalentTo(VoucherTermPricingSchemes.All);

        await CleanAsync(db);
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(-0.0001)]
    public async Task ANegativeRate_IsRefused(decimal rate)
    {
        await using var db = CreateContext();
        await CleanAsync(db);

        db.VoucherTermPricings.Add(Tier(VoucherTermPricingSchemes.RenewalRate, "1m", true, rate));

        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();

        await CleanAsync(db);
    }

    [Fact]
    public async Task AnUnknownSchemeOrTermCode_IsRefused()
    {
        await using var db = CreateContext();
        await CleanAsync(db);

        db.VoucherTermPricings.Add(Tier("whatever", "1w", true, 10m));
        var badScheme = async () => await db.SaveChangesAsync();
        await badScheme.Should().ThrowAsync<DbUpdateException>();

        await CleanAsync(db);
        db.ChangeTracker.Clear();

        db.VoucherTermPricings.Add(Tier(VoucherTermPricingSchemes.RenewalRate, "7d", true, 10m));
        var badTerm = async () => await db.SaveChangesAsync();
        await badTerm.Should().ThrowAsync<DbUpdateException>();

        await CleanAsync(db);
    }

    [Fact]
    public async Task AnEndBeforeItsStart_IsRefused()
    {
        await using var db = CreateContext();
        await CleanAsync(db);

        var row = Tier(VoucherTermPricingSchemes.RenewalRate, "3m", true, 10m);
        row.ValidFrom = DateTime.UtcNow;
        row.ValidTo = DateTime.UtcNow.AddDays(-2);
        db.VoucherTermPricings.Add(row);

        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();

        await CleanAsync(db);
    }

    [Fact]
    public async Task EveryTierCodeTheRenewalTermsEmit_IsAcceptedByTheCheck()
    {
        await using var db = CreateContext();
        await CleanAsync(db);

        // If VoucherRenewalTerms ever grows a term whose Code() is not in the CHECK, the migration
        // INSERT would fail at deploy time. This asserts the two lists agree, so it fails in CI first.
        foreach (var term in VoucherRenewalTerms.All)
        {
            db.VoucherTermPricings.Add(Tier(VoucherTermPricingSchemes.PurchaseTermDiscount, term.Code(), false, 0m));
        }

        await db.SaveChangesAsync();

        var open = await db.VoucherTermPricings.Where(r => r.ValidTo == null).ToListAsync();
        open.Should().HaveCount(VoucherRenewalTerms.All.Count);

        await CleanAsync(db);
    }
}

/// <summary>
/// What the migration itself left behind.
/// </summary>
/// <remarks>
/// A separate class on purpose: it gets its own clone of the migrated schema, so it can assert the
/// post-migration state without the other tests in <see cref="VoucherTermPricingTests"/> having wiped
/// the table first.
/// <para>
/// This is also the assertion that the migration's <c>INSERT</c> is valid SQL. The fixture applies
/// every migration for real, so a malformed statement fails here and every integration test in the
/// suite with it - which is the cheap half. This test covers the other half: that it produced one
/// fail-safe row per tier per scheme rather than none, or all sixteen in one ladder.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class VoucherTermPricingMigrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public VoucherTermPricingMigrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    [Fact]
    public async Task TheMigrationLeavesOneFailSafeRowPerTierPerScheme()
    {
        await using var db = CreateContext();

        var rows = await db.VoucherTermPricings.Where(r => r.ValidTo == null).ToListAsync();

        rows.Should().HaveCount(VoucherTermPricingSchemes.All.Length * VoucherRenewalTerms.All.Count);

        foreach (var scheme in VoucherTermPricingSchemes.All)
        {
            var forScheme = rows.Where(r => r.Scheme == scheme).ToList();
            forScheme.Should().HaveCount(VoucherRenewalTerms.All.Count);
            forScheme.Select(r => r.TermCode).Should().BeEquivalentTo(
                VoucherRenewalTerms.All.Select(t => t.Code()));

            // Empty app_settings in a fresh database means disabled at zero, which is exactly what
            // the old loader did for a missing key - so an unconfigured deploy sells nothing.
            forScheme.Should().OnlyContain(r => !r.Enabled && r.RatePerLiter == 0m);
        }
    }

    [Fact]
    public async Task TheMigrationCreatesTheTableBeforeAnyRowIsWritten()
    {
        // The CREATE and the INSERT are in one Up(), so this is really a statement that the fixture's
        // migrate reached the end of ours rather than failing after creating an empty table.
        await using var db = CreateContext();

        var exists = await db.Database.SqlQueryRaw<int>(
            "SELECT count(*) FROM information_schema.tables WHERE table_name = 'voucher_term_pricing'")
            .ToListAsync();

        exists.Should().ContainSingle().Which.Should().Be(1);
    }
}
using FluentAssertions;
using FuelFlow.Features.Pricing.Rules.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Real-Postgres coverage for <c>pricing_rules</c>.
/// <para>
/// Almost everything worth testing here is a constraint rather than a handler: the table exists so
/// that "which discount applied on 13 March" is answerable and so that one operator cannot silently
/// re-price the catalogue. Both of those are properties of the schema, and InMemory would happily
/// accept every row these tests prove the database rejects.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public class PricingRuleTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public PricingRuleTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    private async Task<Guid> SeedUserAsync(ApplicationDbContext context)
    {
        var roleId = await context.Roles.Where(r => r.Name == SeedRoles.AdminName).Select(r => r.Id).FirstAsync();
        var now = DateTime.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "+380" + Random.Shared.Next(100000000, 999999999).ToString(),
            RoleId = roleId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            IsActive = true,
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>
    /// An existing offer, seeding one only if the catalogue is empty. Creating a second offer for the
    /// same (station, fuel, pool) trips <c>ix_fuel_offers_station_fuel_pool</c> - the catalogue is
    /// unique by construction, and this test is about rules, not about adding catalogue rows.
    /// </summary>
    private async Task<string> SeedOfferAsync(ApplicationDbContext context)
    {
        var existing = await context.FuelOffers.IgnoreQueryFilters()
            .Select(o => o.Id).FirstOrDefaultAsync();
        if (existing is not null) return existing;

        var fuel = await context.FuelTypes.FirstAsync();
        var id = "acq-rule-" + Guid.NewGuid().ToString("N")[..12];
        var now = DateTime.UtcNow;

        context.FuelOffers.Add(new FuelOffer
        {
            Id = id,
            StationId = fuel.StationId,
            FuelTypeId = fuel.Id,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });

        await context.SaveChangesAsync();
        return id;
    }

    /// <summary>
    /// A global approved rule. The approver is a real seeded user and is <em>not</em> the requester —
    /// seeding one was the fix for a failure that turned out to be the FK doing its job: an approver
    /// id that names nobody is exactly what "approved" must not mean.
    /// </summary>
    private async Task<PricingRule> GlobalRuleAsync(
        ApplicationDbContext context, Guid requester, string status = PricingRuleStatuses.Approved)
    {
        var approver = status == PricingRuleStatuses.Approved ? await SeedUserAsync(context) : (Guid?)null;

        return new PricingRule
        {
            Id = Guid.CreateVersion7(),
            RuleKind = PricingRuleKinds.ProfitPerLiter,
            Scope = PricingRuleScopes.Global,
            Value = 5.00m,
            ValidFrom = DateTime.UtcNow.AddDays(-1),
            Status = status,
            RequestedByUserId = requester,
            RequestedAtUtc = DateTime.UtcNow,
            ApprovedByUserId = approver,
            ApprovedAtUtc = approver is null ? null : DateTime.UtcNow,
        };
    }

    // --------------------------------------------------------------------------------------------
    // The closed sets
    // --------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("profit_per_liter")]
    [InlineData("min_discount_per_liter")]
    [InlineData("discount_percent")]
    [InlineData("fixed_price_per_liter")]
    [InlineData("term_discount_per_liter")]
    public async Task Every_documented_rule_kind_is_accepted(string kind)
    {
        using var context = NewContext();
        var requester = await SeedUserAsync(context);

        var rule = await GlobalRuleAsync(context, requester);
        rule.RuleKind = kind;
        context.PricingRules.Add(rule);

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task An_unknown_rule_kind_is_refused()
    {
        using var context = NewContext();
        var requester = await SeedUserAsync(context);

        var rule = await GlobalRuleAsync(context, requester);
        rule.RuleKind = "profit_percent";
        context.PricingRules.Add(rule);

        var act = async () => await context.SaveChangesAsync();

        // A typo here is a price that quietly stops applying.
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    // --------------------------------------------------------------------------------------------
    // Exactly one target, and the scope names it
    // --------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_offer_rule_names_its_offer()
    {
        using var context = NewContext();
        var requester = await SeedUserAsync(context);
        var offerId = await SeedOfferAsync(context);

        var rule = await GlobalRuleAsync(context, requester);
        rule.Scope = PricingRuleScopes.Offer;
        rule.OfferId = offerId;
        context.PricingRules.Add(rule);

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task A_global_rule_may_not_name_an_offer()
    {
        using var context = NewContext();
        var requester = await SeedUserAsync(context);
        var offerId = await SeedOfferAsync(context);

        var rule = await GlobalRuleAsync(context, requester);
        rule.OfferId = offerId;
        context.PricingRules.Add(rule);

        var act = async () => await context.SaveChangesAsync();

        // Without this the row is the quietest possible failure: it matches nothing, and nothing says
        // so. A pricing rule that never applies looks exactly like one priced at zero. (The converse -
        // scope 'offer' WITH an offer - is the valid shape, and is what An_offer_rule_names_its_offer
        // covers. The constraint is on the pairing, not on the column being set.)
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task An_offer_scope_without_an_offer_is_refused()
    {
        using var context = NewContext();
        var requester = await SeedUserAsync(context);

        var rule = await GlobalRuleAsync(context, requester);
        rule.Scope = PricingRuleScopes.Offer;
        rule.OfferId = null;
        context.PricingRules.Add(rule);

        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    // --------------------------------------------------------------------------------------------
    // Four eyes
    // --------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_approved_rule_must_name_its_approver()
    {
        using var context = NewContext();
        var requester = await SeedUserAsync(context);

        var rule = await GlobalRuleAsync(context, requester, PricingRuleStatuses.Approved);
        rule.ApprovedByUserId = null;
        rule.ApprovedAtUtc = null;
        context.PricingRules.Add(rule);

        var act = async () => await context.SaveChangesAsync();

        // This is the whole of four-eyes. Without it "approved" could mean one person clicked twice
        // and the approver column would be decoration.
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task A_draft_rule_may_not_carry_an_approver()
    {
        using var context = NewContext();
        var requester = await SeedUserAsync(context);

        var rule = await GlobalRuleAsync(context, requester, PricingRuleStatuses.Draft);
        rule.ApprovedByUserId = Guid.CreateVersion7();
        rule.ApprovedAtUtc = DateTime.UtcNow;
        context.PricingRules.Add(rule);

        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    // --------------------------------------------------------------------------------------------
    // Values and windows
    // --------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_negative_value_is_refused()
    {
        using var context = NewContext();
        var requester = await SeedUserAsync(context);

        var rule = await GlobalRuleAsync(context, requester);
        rule.Value = -0.01m;
        context.PricingRules.Add(rule);

        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>("a negative price is not a discount");
    }

    [Fact]
    public async Task An_end_before_the_start_is_refused()
    {
        using var context = NewContext();
        var requester = await SeedUserAsync(context);

        var rule = await GlobalRuleAsync(context, requester);
        rule.ValidTo = rule.ValidFrom.AddDays(-1);
        context.PricingRules.Add(rule);

        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task An_open_ended_rule_is_allowed()
    {
        // NULL valid_to means "still in force", which is the common case and must not be confused with
        // a missing value.
        using var context = NewContext();
        var requester = await SeedUserAsync(context);

        var rule = await GlobalRuleAsync(context, requester);
        rule.ValidTo = null;
        context.PricingRules.Add(rule);

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task The_requester_is_required()
    {
        using var context = NewContext();

        var rule = await GlobalRuleAsync(context, Guid.Empty);
        context.PricingRules.Add(rule);

        var act = async () => await context.SaveChangesAsync();

        // A rule nobody asked for has no origin, and "who changed the price" is the first question
        // asked about a pricing change.
        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
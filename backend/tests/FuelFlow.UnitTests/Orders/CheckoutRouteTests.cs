using FluentAssertions;
using FuelFlow.Features.Orders.CreateCheckout;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.SharedKernel.Domain;
using Xunit;

namespace FuelFlow.UnitTests.Orders;

/// <summary>
/// The two rules a checkout applies before it touches the database: which route a request means, and
/// who may mean it.
/// </summary>
/// <remarks>
/// The rest of <see cref="CheckoutRoutes"/> is about a pending order and so belongs to the
/// integration tests. These two are pure, and the second is the security answer to a field the
/// customer sends — it is checked here so that the rule is pinned even where the database is not.
/// </remarks>
public class CheckoutRouteTests
{
    [Fact]
    public void A_Request_That_Did_Not_Say_Is_The_Payment_One()
    {
        // Every bundle in the field posts a body without this field, and every one of them means the
        // bank. A default that invented the other route would let an old app hand out fuel with the
        // acquirer never seeing the sale.
        CheckoutRoutes.Resolve(null).Should().Be(SettlementRoute.Acquirer);
        CheckoutRoutes.Resolve(SettlementRoute.Acquirer).Should().Be(SettlementRoute.Acquirer);
        CheckoutRoutes.Resolve(SettlementRoute.Declared).Should().Be(SettlementRoute.Declared);
    }

    [Fact]
    public void An_Order_From_Before_The_Column_Reads_As_The_Route_It_Could_Only_Have_Been()
    {
        CheckoutRoutes.Of(new Order()).Should().Be(SettlementRoute.Acquirer);
        CheckoutRoutes.Of(new Order { SettlementRoute = SettlementRoute.Declared })
            .Should().Be(SettlementRoute.Declared);
    }

    [Fact]
    public void The_Promo_Route_Without_A_Grant_Is_Refused()
    {
        var act = () => CheckoutRoutes.RequirePromoAccess(SettlementRoute.Declared, new User());

        act.Should().Throw<PromoAccessException>()
            .Which.Message.Should().NotBeNullOrEmpty(
                "a refusal the app cannot show is a refusal the customer works around by phone");
    }

    [Fact]
    public void The_Promo_Route_With_A_Grant_Is_Allowed()
    {
        var granted = new User { PromoAccessGrantedAtUtc = new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc) };

        var act = () => CheckoutRoutes.RequirePromoAccess(SettlementRoute.Declared, granted);

        act.Should().NotThrow("a Manager's decision is the whole gate, and it was passed");
    }

    [Fact]
    public void A_Revoked_Grant_Refuses_From_The_Next_Tap()
    {
        // Revocation sets the column back to NULL, so the refusal has no grace period by construction.
        // What is already ordered is untouched — this decides routes for new orders, nothing more.
        var revoked = new User { PromoAccessGrantedAtUtc = null };

        var act = () => CheckoutRoutes.RequirePromoAccess(SettlementRoute.Declared, revoked);

        act.Should().Throw<PromoAccessException>();
    }

    [Fact]
    public void The_Payment_Route_Is_Never_Refused_For_Want_Of_A_Grant()
    {
        var ungranted = new User();

        var act = () => CheckoutRoutes.RequirePromoAccess(SettlementRoute.Acquirer, ungranted);

        act.Should().NotThrow(
            "a gate on the offer that also stopped the payment route would be a way to lose a sale");
    }
}

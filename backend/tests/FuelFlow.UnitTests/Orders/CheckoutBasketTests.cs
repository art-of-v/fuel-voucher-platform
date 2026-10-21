using FuelFlow.Features.Orders.CreateCheckout;
using Xunit;

namespace FuelFlow.UnitTests.Orders;

/// <summary>
/// The basket's identity: the value the order is guarded by, and the value the database's index
/// refuses a second order for.
/// </summary>
public class CheckoutBasketTests
{
    [Fact]
    public void The_key_of_a_basket_is_the_same_every_time()
    {
        // The whole point: a retry that arrives a second later has to produce the same key, or the
        // guard does not recognise it as the same basket.
        var first = CheckoutBasket.Key(
            CheckoutBasket.Purchase,
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            CheckoutBasket.PurchaseSignature(Guid.Parse("22222222-2222-2222-2222-222222222222"), Guid.Parse("33333333-3333-3333-3333-333333333333"), 10m, 1));

        var second = CheckoutBasket.Key(
            CheckoutBasket.Purchase,
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            CheckoutBasket.PurchaseSignature(Guid.Parse("22222222-2222-2222-2222-222222222222"), Guid.Parse("33333333-3333-3333-3333-333333333333"), 10m, 1));

        Assert.Equal(first, second);
    }

    [Fact]
    public void The_key_of_one_basket_is_not_the_key_of_another()
    {
        var user = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var brand = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var fuel = Guid.Parse("33333333-3333-3333-3333-333333333333");

        var tenLitres = CheckoutBasket.Key(CheckoutBasket.Purchase, user, CheckoutBasket.PurchaseSignature(brand, fuel, 10m, 1));
        var fiveLitres = CheckoutBasket.Key(CheckoutBasket.Purchase, user, CheckoutBasket.PurchaseSignature(brand, fuel, 5m, 1));
        var twoOfThem = CheckoutBasket.Key(CheckoutBasket.Purchase, user, CheckoutBasket.PurchaseSignature(brand, fuel, 10m, 2));
        var anotherFuel = CheckoutBasket.Key(CheckoutBasket.Purchase, user, CheckoutBasket.PurchaseSignature(brand, Guid.Parse("44444444-4444-4444-4444-444444444444"), 10m, 1));

        Assert.NotEqual(tenLitres, fiveLitres);
        Assert.NotEqual(tenLitres, twoOfThem);
        Assert.NotEqual(tenLitres, anotherFuel);
    }

    [Fact]
    public void The_key_of_a_basket_is_not_the_key_of_another_customers_basket()
    {
        var brand = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var fuel = Guid.Parse("33333333-3333-3333-3333-333333333333");

        var mine = CheckoutBasket.Key(
            CheckoutBasket.Purchase,
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            CheckoutBasket.PurchaseSignature(brand, fuel, 10m, 1));

        var theirs = CheckoutBasket.Key(
            CheckoutBasket.Purchase,
            Guid.Parse("99999999-9999-9999-9999-999999999999"),
            CheckoutBasket.PurchaseSignature(brand, fuel, 10m, 1));

        Assert.NotEqual(mine, theirs);
    }

    [Fact]
    public void The_key_of_a_cart_does_not_depend_on_the_order_of_the_lines()
    {
        var user = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var oneWay = CheckoutBasket.Key(
            CheckoutBasket.Cart,
            user,
            CheckoutBasket.CartSignature(["a:1", "b:2", "c:3"]));

        var theOther = CheckoutBasket.Key(
            CheckoutBasket.Cart,
            user,
            CheckoutBasket.CartSignature(["c:3", "a:1", "b:2"]));

        Assert.Equal(oneWay, theOther);
    }

    [Fact]
    public void The_kinds_of_basket_do_not_share_a_key()
    {
        // A purchase, a cart and a renewal are three different baskets. A key that could be one of
        // them would let a renewal take a purchase's basket hostage.
        var user = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var signature = "the same thing";

        var purchase = CheckoutBasket.Key(CheckoutBasket.Purchase, user, signature);
        var cart = CheckoutBasket.Key(CheckoutBasket.Cart, user, signature);
        var renewal = CheckoutBasket.Key(CheckoutBasket.Renewal, user, signature);

        Assert.NotEqual(purchase, cart);
        Assert.NotEqual(cart, renewal);
        Assert.NotEqual(purchase, renewal);
    }

    [Fact]
    public void The_key_of_a_basket_fits_the_column()
    {
        var key = CheckoutBasket.Key(
            CheckoutBasket.Purchase,
            Guid.NewGuid(),
            CheckoutBasket.PurchaseSignature(Guid.NewGuid(), Guid.NewGuid(), 123.456m, 1000));

        // The order's own key is the basket's key plus a suffix, and the column holds 150.
        Assert.True(key.Length + 1 + 32 <= 150, $"the key was {key.Length} characters");
    }
}

namespace FuelFlow.SharedKernel;

/// <summary>
/// Currency unit conversions. The FuelFlow domain model stores money in whole-UAH
/// integers (orders.price, line-item unit prices, fuel packages, fuel type prices);
/// kopeck integers are required only at the Monobank merchant API boundary
/// (invoice creation, refunds, webhooks). Every conversion between the two units
/// must go through these helpers so unit mistakes stay greppable in one place.
/// </summary>
public static class Money
{
    public static long ToKopecks(int uah) => (long)uah * 100;

    public static long ToKopecks(long uah) => uah * 100;

    public static decimal ToKopecks(decimal uah) => uah * 100m;

    public static decimal FromKopecks(long kopecks) => kopecks / 100m;
}

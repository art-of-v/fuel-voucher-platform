using FuelFlow.SharedKernel.Domain;
using Xunit;

namespace FuelFlow.UnitTests.SharedKernel;

/// <summary>
/// The three money rules that decide what a voucher is worth: the weighted average the shop prices off,
/// the reduction a customer payment causes, and the value transfer an exchange performs.
/// </summary>
public class VoucherCostingTests
{
    // ── weighted average ─────────────────────────────────────────────────────

    [Fact]
    public void BlendedCost_weightsBatchesByLiters()
    {
        // 100 L at 90.00 and 300 L at 96.00 → (100*90 + 300*96)/400 = 94.50
        var pool = new[]
        {
            (Liters: 100m, CostPerLiter: 90.00m),
            (Liters: 300m, CostPerLiter: 96.00m),
        };

        Assert.Equal(94.50m, PurchaseBatchCosting.BlendedCostPerLiter(pool));
    }

    [Fact]
    public void BlendedCost_ofASingleVoucherIsItsOwnCost()
    {
        var pool = new[] { (Liters: 10m, CostPerLiter: 91.25m) };

        Assert.Equal(91.25m, PurchaseBatchCosting.BlendedCostPerLiter(pool));
    }

    [Fact]
    public void BlendedCost_isNullWhenNothingCarriesACost()
    {
        Assert.Null(PurchaseBatchCosting.BlendedCostPerLiter([]));
    }

    [Fact]
    public void BlendedCost_ignoresZeroAndNegativeLiters()
    {
        var pool = new[]
        {
            (Liters: 0m, CostPerLiter: 50.00m),
            (Liters: -5m, CostPerLiter: 50.00m),
        };

        Assert.Null(PurchaseBatchCosting.BlendedCostPerLiter(pool));
    }

    /// <summary>
    /// The whole point of per-voucher cost: five vouchers of one brand bought at five prices move the
    /// blended price by five different amounts, each in proportion to its own liters.
    /// </summary>
    [Fact]
    public void BlendedCost_letsEachVoucherWeighDifferently()
    {
        var pool = new[]
        {
            (Liters: 100m, CostPerLiter: 88.00m),
            (Liters: 100m, CostPerLiter: 91.50m),
            (Liters: 100m, CostPerLiter: 93.00m),
            (Liters: 100m, CostPerLiter: 95.25m),
            (Liters: 100m, CostPerLiter: 97.00m),
        };

        Assert.Equal(92.95m, PurchaseBatchCosting.BlendedCostPerLiter(pool));
    }

    // ── customer payment devalues a voucher ─────────────────────────────────

    [Fact]
    public void CustomerPayment_reducesCostByPaidPerLiter()
    {
        // 37.50 paid on a 10 L voucher bought at 90.00/L → 90.00 − 3.75
        Assert.Equal(86.25m, VoucherCosting.AfterCustomerPayment(90.00m, 10m, 37.50m));
    }

    [Fact]
    public void CustomerPayment_compoundsAcrossSuccessiveRenewals()
    {
        var afterFirst = VoucherCosting.AfterCustomerPayment(90.00m, 10m, 20m);   // 88.00
        var afterSecond = VoucherCosting.AfterCustomerPayment(afterFirst, 10m, 30m); // 85.00

        Assert.Equal(88.00m, afterFirst);
        Assert.Equal(85.00m, afterSecond);
    }

    [Fact]
    public void CustomerPayment_neverPushesCostBelowZero()
    {
        // A cheap voucher extended repeatedly must not produce a negative cost, which would drag the
        // blended price below what any supplier charges.
        var reduced = VoucherCosting.AfterCustomerPayment(5.00m, 10m, 500m);

        Assert.Equal(0m, reduced);
    }

    [Fact]
    public void CustomerPayment_ofZeroLeavesCostUnchanged()
    {
        Assert.Equal(90.00m, VoucherCosting.AfterCustomerPayment(90.00m, 10m, 0m));
    }

    [Fact]
    public void CustomerPayment_onAVoucherWithoutLitersLeavesCostUnchanged()
    {
        Assert.Equal(90.00m, VoucherCosting.AfterCustomerPayment(90.00m, 0m, 37.50m));
    }

    // ── exchange transfers value ─────────────────────────────────────────────

    [Fact]
    public void Exchange_addsTheSurchargeSpreadOverNewLiters()
    {
        // 50 L of new paper, 100.00 UAH доплата → 2.00 per new liter
        Assert.Equal(92.00m, VoucherCosting.AfterExchange(90.00m, 100.00m, 50m));
    }

    /// <summary>
    /// The conservation invariant: retiring the old vouchers and adding the new ones plus the доплата
    /// leaves the pool's total value unchanged apart from what was actually paid.
    /// </summary>
    [Fact]
    public void Exchange_conservesTotalValue()
    {
        var oldCosts = new[] { 88.00m, 91.50m, 93.00m, 95.25m, 97.00m };
        const int litersEach = 10;
        var totalNewLiters = oldCosts.Length * litersEach;
        const decimal surcharge = 100.00m;

        var valueOut = oldCosts.Sum(c => c * litersEach);
        var valueIn = oldCosts.Sum(c => VoucherCosting.AfterExchange(c, surcharge, totalNewLiters) * litersEach);

        Assert.Equal(valueOut + surcharge, valueIn);
    }

    [Fact]
    public void Exchange_withNoSurchargeCarriesTheCostOverUnchanged()
    {
        Assert.Equal(93.00m, VoucherCosting.AfterExchange(93.00m, 0m, 50m));
    }

    [Fact]
    public void Exchange_withoutNewLitersLeavesCostUnchanged()
    {
        Assert.Equal(93.00m, VoucherCosting.AfterExchange(93.00m, 100.00m, 0m));
    }
}
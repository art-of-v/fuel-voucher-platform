using FluentAssertions;
using FuelFlow.Features.Vouchers.Exchange;

namespace FuelFlow.UnitTests.Vouchers.Exchange;

public sealed class VoucherExchangePairingTests
{
    private static VoucherExchangePairing.Candidate Old(string number, string provider = "okko", string fuel = "okko-a95", decimal liters = 50m)
        => new(Guid.NewGuid(), provider, fuel, liters, number);

    private static VoucherExchangePairing.Candidate New(string number, string provider = "okko", string fuel = "okko-a95", decimal liters = 50m)
        => new(Guid.NewGuid(), provider, fuel, liters, number);

    [Fact]
    public void Pair_EqualCountsSameBucket_PairsAllOneToOne()
    {
        var olds = new[] { Old("O-2"), Old("O-1") };
        var news = new[] { New("N-1"), New("N-2") };

        var result = VoucherExchangePairing.Pair(olds, news);

        result.PairedCount.Should().Be(2);
        result.UnpairedOldCount.Should().Be(0);
        result.UnpairedNewCount.Should().Be(0);
        result.Pairs.Should().HaveCount(2);
        result.Pairs.Should().OnlyContain(p => p.NewId != null);
    }

    [Fact]
    public void Pair_IsDeterministicBySortedVoucherNumber()
    {
        var o1 = Old("O-1");
        var o2 = Old("O-2");
        var n1 = New("N-1");
        var n2 = New("N-2");

        // Smallest old number pairs with smallest new number regardless of input order.
        var result = VoucherExchangePairing.Pair(new[] { o2, o1 }, new[] { n2, n1 });

        result.Pairs.Single(p => p.OldId == o1.Id).NewId.Should().Be(n1.Id);
        result.Pairs.Single(p => p.OldId == o2.Id).NewId.Should().Be(n2.Id);
    }

    [Fact]
    public void Pair_MoreOldThanNew_LeftoverOldsUnpaired()
    {
        var olds = new[] { Old("O-1"), Old("O-2"), Old("O-3") };
        var news = new[] { New("N-1") };

        var result = VoucherExchangePairing.Pair(olds, news);

        result.PairedCount.Should().Be(1);
        result.UnpairedOldCount.Should().Be(2);
        result.UnpairedNewCount.Should().Be(0);
        result.Pairs.Should().HaveCount(3);
        result.Pairs.Count(p => p.NewId == null).Should().Be(2);
    }

    [Fact]
    public void Pair_MoreNewThanOld_LeftoverNewsEnterStockNoRow()
    {
        var olds = new[] { Old("O-1") };
        var news = new[] { New("N-1"), New("N-2"), New("N-3") };

        var result = VoucherExchangePairing.Pair(olds, news);

        result.PairedCount.Should().Be(1);
        result.UnpairedOldCount.Should().Be(0);
        result.UnpairedNewCount.Should().Be(2);
        // One row per OLD only — leftover news get no row.
        result.Pairs.Should().HaveCount(1);
    }

    [Fact]
    public void Pair_DifferentBuckets_DoNotCrossPair()
    {
        // Same provider, different fuel → different bucket; same fuel, different liters → different bucket.
        var oldA95 = Old("O-1", fuel: "okko-a95", liters: 50m);
        var oldDp = Old("O-2", fuel: "okko-dp", liters: 50m);
        var newA95Diff = New("N-1", fuel: "okko-a95", liters: 40m); // different liters
        var newDp = New("N-2", fuel: "okko-dp", liters: 50m);

        var result = VoucherExchangePairing.Pair(new[] { oldA95, oldDp }, new[] { newA95Diff, newDp });

        // Only the dp pair matches; the a95 old (50L) finds no 50L a95 new.
        result.PairedCount.Should().Be(1);
        result.Pairs.Single(p => p.OldId == oldDp.Id).NewId.Should().Be(newDp.Id);
        result.Pairs.Single(p => p.OldId == oldA95.Id).NewId.Should().BeNull();
        result.UnpairedOldCount.Should().Be(1);
        result.UnpairedNewCount.Should().Be(1);
    }

    [Fact]
    public void Pair_ProviderBucketingIsCaseInsensitive()
    {
        var old = Old("O-1", provider: "OKKO");
        var fresh = New("N-1", provider: "okko");

        var result = VoucherExchangePairing.Pair(new[] { old }, new[] { fresh });

        result.PairedCount.Should().Be(1);
        result.Pairs.Single().NewId.Should().Be(fresh.Id);
    }

    [Fact]
    public void Pair_EmptyNews_AllOldsUnpaired()
    {
        var olds = new[] { Old("O-1"), Old("O-2") };

        var result = VoucherExchangePairing.Pair(olds, Array.Empty<VoucherExchangePairing.Candidate>());

        result.PairedCount.Should().Be(0);
        result.UnpairedOldCount.Should().Be(2);
        result.UnpairedNewCount.Should().Be(0);
        result.Pairs.Should().OnlyContain(p => p.NewId == null);
    }
}

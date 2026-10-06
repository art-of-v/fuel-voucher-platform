using FluentAssertions;
using FuelFlow.Features.Vouchers.History;
using Xunit;
using Node = FuelFlow.Features.Vouchers.History.VoucherHistoryProjector.RenewalNode;
using Purchase = FuelFlow.Features.Vouchers.History.VoucherHistoryProjector.PurchaseNode;

namespace FuelFlow.UnitTests.Vouchers.History;

public class VoucherHistoryProjectorTests
{
    private static readonly DateTime Jan1 = new(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Feb1 = new(2026, 2, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Mar1 = new(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ResolveRootVoucherId_returns_the_voucher_itself_when_never_replaced()
    {
        var v = Guid.NewGuid();
        // an extend keeps the same id, so it does not move the root
        var renewals = new[] { new Node(v, v, Feb1, "1m", 300m, null, null) };
        VoucherHistoryProjector.ResolveRootVoucherId(v, renewals).Should().Be(v);
        VoucherHistoryProjector.ResolveRootVoucherId(v, Array.Empty<Node>()).Should().Be(v);
    }

    [Fact]
    public void ResolveRootVoucherId_walks_back_across_replace_swaps_to_the_original()
    {
        var original = Guid.NewGuid();
        var mid = Guid.NewGuid();
        var current = Guid.NewGuid();
        // original -> replaced into mid -> replaced into current
        var renewals = new[]
        {
            new Node(original, mid, Feb1, "2m", 500m, null, null),
            new Node(mid, current, Mar1, "2m", 500m, null, null),
        };
        VoucherHistoryProjector.ResolveRootVoucherId(current, renewals).Should().Be(original);
    }

    [Fact]
    public void A_plain_purchase_with_no_renewals_is_a_single_purchase_event()
    {
        var voucher = Guid.NewGuid();

        var history = VoucherHistoryProjector.Build(
            voucher, 50m, [],
            id => id == voucher ? new Purchase(Jan1, 2000m) : null);

        history.Should().ContainSingle();
        var e = history[0];
        e.Type.Should().Be(VoucherHistoryEventType.Purchase);
        e.Date.Should().Be(Jan1);
        e.Liters.Should().Be(50m);
        e.Amount.Should().Be(2000m);
        e.ValidFrom.Should().BeNull();
        e.ValidTo.Should().BeNull();
        e.TermCode.Should().BeNull();
    }

    [Fact]
    public void An_extend_keeps_the_same_voucher_and_adds_a_renewal_event()
    {
        var voucher = Guid.NewGuid();
        var renewals = new[]
        {
            new Node(voucher, voucher, Feb1, "1m", 300m,
                new DateOnly(2026, 2, 5), new DateOnly(2026, 3, 5)),
        };

        var history = VoucherHistoryProjector.Build(
            voucher, 50m, renewals,
            id => id == voucher ? new Purchase(Jan1, 2000m) : null);

        history.Should().HaveCount(2);
        history[0].Type.Should().Be(VoucherHistoryEventType.Purchase);

        var renewal = history[1];
        renewal.Type.Should().Be(VoucherHistoryEventType.Renewal);
        renewal.Date.Should().Be(Feb1);
        renewal.Amount.Should().Be(300m);
        renewal.TermCode.Should().Be("1m");
        renewal.ValidFrom.Should().Be(new DateOnly(2026, 2, 5));
        renewal.ValidTo.Should().Be(new DateOnly(2026, 3, 5));
    }

    [Fact]
    public void A_replace_walks_across_the_swap_so_the_whole_lineage_is_one_history()
    {
        // old voucher was purchased, then a replace issued `current` in its place.
        var old = Guid.NewGuid();
        var current = Guid.NewGuid();
        var renewals = new[]
        {
            new Node(old, current, Feb1, "2m", 500m,
                new DateOnly(2026, 1, 20), new DateOnly(2026, 3, 20)),
        };

        var history = VoucherHistoryProjector.Build(
            current, 40m, renewals,
            id => id == old ? new Purchase(Jan1, 1600m) : null);

        history.Should().HaveCount(2);
        history[0].Type.Should().Be(VoucherHistoryEventType.Purchase);
        history[0].Date.Should().Be(Jan1);
        history[0].Amount.Should().Be(1600m);
        history[1].Type.Should().Be(VoucherHistoryEventType.Renewal);
        history[1].ValidTo.Should().Be(new DateOnly(2026, 3, 20));
    }

    [Fact]
    public void Events_are_ordered_oldest_first_across_a_purchase_then_two_renewals()
    {
        var old = Guid.NewGuid();
        var current = Guid.NewGuid();
        var renewals = new[]
        {
            // extend of the current voucher (Mar), and the earlier replace that created it (Feb)
            new Node(current, current, Mar1, "1w", 120m,
                new DateOnly(2026, 3, 20), new DateOnly(2026, 3, 27)),
            new Node(old, current, Feb1, "2m", 500m,
                new DateOnly(2026, 1, 20), new DateOnly(2026, 3, 20)),
        };

        var history = VoucherHistoryProjector.Build(
            current, 40m, renewals,
            id => id == old ? new Purchase(Jan1, 1600m) : null);

        history.Select(e => e.Date).Should().BeInAscendingOrder();
        history.Should().HaveCount(3);
        history[0].Type.Should().Be(VoucherHistoryEventType.Purchase);
        history[1].Date.Should().Be(Feb1);
        history[2].Date.Should().Be(Mar1);
    }

    [Fact]
    public void A_lineage_with_no_recorded_purchase_still_lists_its_renewals()
    {
        // operator-issued stock (no owning purchase order) that was later extended.
        var voucher = Guid.NewGuid();
        var renewals = new[]
        {
            new Node(voucher, voucher, Feb1, "1m", 300m,
                new DateOnly(2026, 2, 5), new DateOnly(2026, 3, 5)),
        };

        var history = VoucherHistoryProjector.Build(
            voucher, 50m, renewals, _ => null);

        history.Should().ContainSingle();
        history[0].Type.Should().Be(VoucherHistoryEventType.Renewal);
    }

    [Fact]
    public void Null_snapshots_on_legacy_rows_yield_a_renewal_event_without_a_date_range()
    {
        var voucher = Guid.NewGuid();
        var renewals = new[]
        {
            new Node(voucher, voucher, Feb1, "1m", null, null, null),
        };

        var history = VoucherHistoryProjector.Build(
            voucher, 50m, renewals,
            id => id == voucher ? new Purchase(Jan1, 2000m) : null);

        var renewal = history.Single(e => e.Type == VoucherHistoryEventType.Renewal);
        renewal.Amount.Should().BeNull();
        renewal.ValidFrom.Should().BeNull();
        renewal.ValidTo.Should().BeNull();
    }
}

using FuelFlow.Features.Ledger;
using FuelFlow.Features.Vouchers.Redemptions;
using FuelFlow.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace FuelFlow.IntegrationTests;

public static class TestRedemptions
{
    public static VoucherRedemptionService For(ApplicationDbContext context)
    {
        var journal = TestJournal.For(context);
        var poster = new RedemptionJournalPoster(
            context,
            journal,
            NullLogger<RedemptionJournalPoster>.Instance);

        return new VoucherRedemptionService(
            context,
            poster,
            journal,
            NullLogger<VoucherRedemptionService>.Instance);
    }
}

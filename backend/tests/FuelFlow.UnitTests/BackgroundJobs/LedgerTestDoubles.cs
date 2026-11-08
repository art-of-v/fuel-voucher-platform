using FuelFlow.Features.Ledger;
using FuelFlow.Features.Orders.SettlePaidOrder;
using FuelFlow.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace FuelFlow.UnitTests.BackgroundJobs;

/// <summary>
/// The ledger writers that a hand-built fulfilment service carries, composed over the context the
/// test is already holding.
/// </summary>
/// <remarks>
/// Real objects, not stubs. On an in-memory store the mapping rules are not seeded, so a release
/// composed this way reports its refusal and changes nothing — which is the only answer that keeps
/// the delivery under test the thing being tested.
/// </remarks>
internal static class LedgerTestDoubles
{
    internal static ReleaseVatOutsideThePerimeterCommandHandler VatRelease(ApplicationDbContext context) =>
        new(
            context,
            new JournalPostingService(context, NullLogger<JournalPostingService>.Instance),
            NullLogger<ReleaseVatOutsideThePerimeterCommandHandler>.Instance);
}

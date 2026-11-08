using FuelFlow.Features.Ledger;
using FuelFlow.Features.Orders.SettlePaidOrder;
using FuelFlow.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The ledger writers that a hand-built fulfilment service carries, composed over the context the
/// test is already holding.
/// </summary>
/// <remarks>
/// These are real, not stubs. A release that is stubbed out cannot move the balance the test is
/// asserting on, and the point of a delivery-time VAT entry is the balance.
/// </remarks>
internal static class LedgerTestDoubles
{
    internal static ReleaseVatOutsideThePerimeterCommandHandler VatRelease(ApplicationDbContext context) =>
        new(
            context,
            new JournalPostingService(context, NullLogger<JournalPostingService>.Instance),
            NullLogger<ReleaseVatOutsideThePerimeterCommandHandler>.Instance);
}

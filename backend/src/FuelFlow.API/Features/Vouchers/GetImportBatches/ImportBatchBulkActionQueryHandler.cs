using FuelFlow.Features.Vouchers.Events;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.GetImportBatches;

/// <summary>
/// Activates or deactivates every voucher of an import batch, decided by the server.
/// </summary>
/// <remarks>
/// <para>
/// The admin's two buttons used to act on the ids the tab had loaded, which was the whole batch
/// only because the list was unbounded. The moment the list becomes a page, those ids are a page
/// of the batch, and a button that says "activate all" and activates a page is a button that
/// lies.
/// </para>
/// <para>
/// So the action names the import and the statuses it applies to, and the database decides which
/// rows that is. The rules are the ones the id-based action already had:
/// </para>
/// <list type="bullet">
/// <item>activating refuses a batch that is not fully costed - a partly-entered batch must not put
/// its costed part on sale;</item>
/// <item>a status change writes an event per voucher, the way every other status change does.</item>
/// </list>
/// <para>
/// The work is bounded by the batch, which is what it always was: the batch is one supplier's
/// delivery, and the review's own words for it are that it is "bounded by the batch, and the
/// admin's activate-all / deactivate-all act on the WHOLE batch".
/// </para>
/// </remarks>
public sealed class ImportBatchBulkActionQueryHandler
{
    /// <summary>
    /// The statuses a voucher can be activated FROM - the same set the button used to filter by.
    /// </summary>
    private static readonly VoucherStatus[] ActivatableFrom =
    [
        VoucherStatus.Imported,
        VoucherStatus.VerifiedWithWarnings,
        VoucherStatus.Expired,
        VoucherStatus.Deactivated
    ];

    /// <summary>
    /// The statuses a voucher can be deactivated FROM - the same set the button used to filter by.
    /// </summary>
    private static readonly VoucherStatus[] DeactivatableFrom =
    [
        VoucherStatus.Imported,
        VoucherStatus.Available,
        VoucherStatus.VerifiedWithWarnings
    ];

    private readonly ApplicationDbContext _context;

    public ImportBatchBulkActionQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ImportBatchBulkActionResult> HandleAsync(
        ImportBatchBulkActionQuery query,
        CancellationToken cancellationToken = default)
    {
        var from = query.Action switch
        {
            "activate" => ActivatableFrom,
            "deactivate" => DeactivatableFrom,
            _ => null
        };

        if (from is null)
        {
            return new ImportBatchBulkActionResult(false, $"Unknown action '{query.Action}'.", 0);
        }

        var vouchers = await _context.FuelVouchers
            .IgnoreQueryFilters()
            .Where(v => v.ImportJobId == query.ImportId && from.Contains(v.Status))
            .ToListAsync(cancellationToken);

        if (vouchers.Count == 0)
        {
            return new ImportBatchBulkActionResult(true, null, 0);
        }

        if (query.Action == "activate")
        {
            // The same gate the id-based action has: never put on sale stock that has no cost of
            // its own, because the pricing engine would have nothing to price from. The cost lives
            // on the voucher, so the check is per voucher - a batch is only costed once every
            // voucher in it is, and a partly-entered batch must not put the costed part on sale.
            var uncosted = vouchers.Where(v => v.CostPerLiter is null).ToList();
            if (uncosted.Count > 0)
            {
                return new ImportBatchBulkActionResult(
                    false,
                    $"Cannot activate - enter the cost first for {uncosted.Count} voucher(s): " +
                    string.Join(", ", uncosted.Take(10).Select(v => v.VoucherNumber)),
                    0);
            }
        }

        var to = query.Action == "activate" ? VoucherStatus.Available : VoucherStatus.Deactivated;
        var now = DateTime.UtcNow;
        var actor = query.ActingAdminUserId is { } a && a != Guid.Empty ? (Guid?)a : null;

        foreach (var voucher in vouchers)
        {
            var fromStatus = voucher.Status;
            voucher.Status = to;
            voucher.UpdatedAtUtc = now;

            if (fromStatus == to || actor is null)
            {
                continue;
            }

            _context.VoucherEvents.Add(new VoucherEvent
            {
                Id = Guid.CreateVersion7(),
                VoucherId = voucher.Id,
                EventType = VoucherEventTypes.StatusChanged,
                FromStatus = fromStatus.ToString(),
                ToStatus = to.ToString(),
                ActorKind = VoucherActorKinds.User,
                ActorUserId = actor,
                OccurredAtUtc = now
            });
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new ImportBatchBulkActionResult(true, null, vouchers.Count);
    }
}

/// <summary>What a batch action did, and what stopped it if it did not.</summary>
public sealed record ImportBatchBulkActionResult(bool Success, string? Error, int Affected);

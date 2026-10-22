using FuelFlow.Features.Brands;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.Suppliers.UpdateSupplier;

/// <summary>
/// Changes a supplier, and the audit row that says it happened.
/// </summary>
/// <remarks>
/// The supply route is the one field a change is refused for: it is fixed at creation because every
/// voucher imported from this supplier carries a copy of it, and the sentence that refuses the
/// change says why a supplier cannot move.
/// </remarks>
public sealed class UpdateSupplierCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly AuditEventService _events;

    public UpdateSupplierCommandHandler(ApplicationDbContext context, AuditEventService events)
    {
        _context = context;
        _events = events;
    }

    public async Task<SupplierWriteResult> HandleAsync(
        UpdateSupplierCommand command,
        CancellationToken ct = default)
    {
        var request = command.Request;

        if (SupplierRules.Validate(request) is { } error)
            return SupplierWriteResult.Invalid(error);

        var supplier = await _context.Suppliers.AsTracking().FirstOrDefaultAsync(s => s.Id == command.Id, ct);
        if (supplier is null) return SupplierWriteResult.NotFound();

        // UpdateSupplierRequest inherits from CreateSupplierRequest, so this endpoint can be handed a
        // route whether or not the caller means to. The column is immutable - the database would
        // refuse with a raw exception, which is a 500 and a stack trace in the Error Logs rather than
        // an answer. So it is refused here, by name, and the sentence says why a supplier cannot move:
        // every voucher already imported from it holds a copy.
        //
        // Sent and equal is a no-op rather than an error, because a client that echoes back what it
        // read should not be punished for it.
        if (!string.IsNullOrWhiteSpace(request.AcquisitionSource)
            && !string.Equals(
                request.AcquisitionSource.Trim().ToLowerInvariant(),
                supplier.AcquisitionSource,
                StringComparison.Ordinal))
        {
            return SupplierWriteResult.Invalid(
                $"The supply route of a supplier cannot change: it is fixed when the supplier is created, " +
                $"because every voucher already imported from them carries a copy. " +
                $"'{supplier.AcquisitionSource}' stays. Add a new supplier if the arrangement has changed.");
        }

        var name = request.Name!.Trim();
        if (await _context.Suppliers.AnyAsync(s => s.Id != command.Id && s.Name == name, ct))
            return SupplierWriteResult.NameTaken(name);

        supplier.Name = name;
        supplier.LegalForm = request.LegalForm?.Trim();
        supplier.Phone = request.Phone?.Trim();
        supplier.Email = request.Email?.Trim();
        supplier.EdrIpn = SupplierRules.NormaliseEdrIpn(request.EdrIpn);
        supplier.Rnkrr = request.Rnkrr?.Trim();
        supplier.Address = request.Address?.Trim();
        supplier.Notes = request.Notes?.Trim();
        if (request.IsActive.HasValue) supplier.IsActive = request.IsActive.Value;
        supplier.UpdatedAtUtc = SystemClock.UtcNow;

        // AsTracking is load-bearing, not decoration. The context's default is TrackAll, so without it
        // these property changes reach no change tracker, SaveChanges writes nothing for the row, and the
        // response below - built from this same instance - still shows the new values. The outbox row added
        // in RecordAsync always persists, which is what made the audit trail claim success while the table
        // disagreed. Same reason as ConfirmVoucherExchangeCommandHandler and the Settings handlers.
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (SupplierRules.IsUniqueViolation(ex))
        {
            return SupplierWriteResult.NameTaken(name);
        }

        await SupplierAudit.RecordAsync(
            _events, "SupplierUpdated", supplier, command.ActorUserId, command.ActorUserName, ct);

        return SupplierWriteResult.Done(SupplierDto.From(supplier));
    }
}

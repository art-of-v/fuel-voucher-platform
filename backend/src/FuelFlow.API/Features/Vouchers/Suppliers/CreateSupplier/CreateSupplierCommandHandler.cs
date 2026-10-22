using FuelFlow.Features.Brands;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.Suppliers.CreateSupplier;

/// <summary>
/// Records a new supplier, and the audit row that says it happened.
/// </summary>
/// <remarks>
/// The rules live here rather than in the controller: a supplier is a counterparty we settle with,
/// and the name, the requisites and the supply route are what make one usable - so the refusals are
/// business rules, not HTTP details.
/// </remarks>
public sealed class CreateSupplierCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly AuditEventService _events;

    public CreateSupplierCommandHandler(ApplicationDbContext context, AuditEventService events)
    {
        _context = context;
        _events = events;
    }

    public async Task<SupplierWriteResult> HandleAsync(
        CreateSupplierCommand command,
        CancellationToken ct = default)
    {
        var request = command.Request;

        if (SupplierRules.Validate(request) is { } error)
            return SupplierWriteResult.Invalid(error);

        var name = request.Name!.Trim();

        // The operator picks a supplier by name, so a duplicate is a refusal, not a new row. Left to
        // Postgres it arrives as a 23505 on SaveChanges: a 500 with a stack trace in the Error Logs and
        // nothing on screen but "Something went wrong". The comparison mirrors the unique index exactly,
        // so the API never refuses a name the database itself would have stored.
        if (await _context.Suppliers.AnyAsync(s => s.Name == name, ct))
            return SupplierWriteResult.NameTaken(name);

        var now = SystemClock.UtcNow;
        var supplier = new Supplier
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            // Stated once, here, and never again: the import copies this onto every voucher it creates
            // from this supplier, so this is the only moment the route is decided.
            AcquisitionSource = string.IsNullOrWhiteSpace(request.AcquisitionSource)
                ? AcquisitionSources.Default
                : request.AcquisitionSource.Trim().ToLowerInvariant(),
            LegalForm = request.LegalForm?.Trim(),
            Phone = request.Phone?.Trim(),
            Email = request.Email?.Trim(),
            EdrIpn = SupplierRules.NormaliseEdrIpn(request.EdrIpn),
            Rnkrr = request.Rnkrr?.Trim(),
            Address = request.Address?.Trim(),
            Notes = request.Notes?.Trim(),
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _context.Suppliers.Add(supplier);
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (SupplierRules.IsUniqueViolation(ex))
        {
            return SupplierWriteResult.NameTaken(name);
        }

        await SupplierAudit.RecordAsync(
            _events, "SupplierCreated", supplier, command.ActorUserId, command.ActorUserName, ct);

        return SupplierWriteResult.Done(SupplierDto.From(supplier));
    }
}

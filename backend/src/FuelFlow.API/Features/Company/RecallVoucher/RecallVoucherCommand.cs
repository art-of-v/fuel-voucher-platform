using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Company.RecallVoucher;

public sealed record RecallVoucherCommand(Guid OwnerUserId, Guid VoucherId);

public sealed record RecallVoucherResult(string Status, string? ErrorMessage = null);

public sealed class RecallVoucherCommandHandler
{
    private readonly ApplicationDbContext _context;

    public RecallVoucherCommandHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<RecallVoucherResult> HandleAsync(RecallVoucherCommand command, CancellationToken cancellationToken = default)
    {
        var legalEntityId = await _context.LegalEntities
            .AsNoTracking()
            .Where(x => x.UserId == command.OwnerUserId)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (legalEntityId is null)
        {
            return new RecallVoucherResult("OwnerCompanyNotFound", "Owner does not have a legal entity profile.");
        }

        var voucher = await _context.FuelVouchers
            .FirstOrDefaultAsync(x => x.Id == command.VoucherId, cancellationToken);

        if (voucher is null)
        {
            return new RecallVoucherResult("NotFound", "Voucher not found.");
        }

        if (voucher.LegalEntityId != legalEntityId.Value || voucher.AssignedToUserId != command.OwnerUserId)
        {
            return new RecallVoucherResult("Forbidden", "Voucher does not belong to this company owner.");
        }

        if (voucher.Status != VoucherStatus.Assigned || voucher.WorkerUserId is null)
        {
            return new RecallVoucherResult("InvalidState", "Only assigned gifted vouchers can be recalled.");
        }

        voucher.WorkerUserId = null;
        voucher.UpdatedAtUtc = DateTime.UtcNow;

        _context.Update(voucher);
        await _context.SaveChangesAsync(cancellationToken);

        return new RecallVoucherResult("Success");
    }
}

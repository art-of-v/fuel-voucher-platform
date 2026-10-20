using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.GetUserVouchers;

public sealed class GetUserVouchersCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly IQrGenerator _qrGenerator;
    private readonly ILogger<GetUserVouchersCommandHandler> _logger;

    public GetUserVouchersCommandHandler(
        ApplicationDbContext context,
        IQrGenerator qrGenerator,
        ILogger<GetUserVouchersCommandHandler> logger)
    {
        _context = context;
        _qrGenerator = qrGenerator;
        _logger = logger;
    }

    public async Task<GetUserVouchersResponse> HandleAsync(
        GetUserVouchersCommand command,
        CancellationToken cancellationToken = default)
    {
        // The filter is built ONCE and used twice: once to count, once to read the page.
        // The count is asked of the filtered query WITHOUT the includes, because EF logs
        // "the include operation was ignored" for a count over an included query - and the
        // count is the one query here that must stay cheap.
        var wallet = _context.FuelVouchers
            .Where(v => v.AssignedToUserId == command.UserId || v.WorkerUserId == command.UserId)
            // Owners also see vouchers they froze on a worker (Blocked) so they can unblock
            // them (#103 S3b); workers never see a blocked voucher — it drops out of their wallet
            // until the owner lifts the freeze.
            .Where(v => v.Status == VoucherStatus.Assigned
                        || v.Status == VoucherStatus.Used
                        || (v.Status == VoucherStatus.Blocked && v.AssignedToUserId == command.UserId));

        var totalCount = await wallet.CountAsync(cancellationToken);

        var entities = await wallet
            .AsNoTracking()
            .Include(v => v.QrParameters)
            .Include(v => v.WorkerUser)
            .Include(v => v.FuelType)
            .OrderByDescending(v => v.CreatedAtUtc)
            // The tie-break. Without it a voucher could appear on two pages or on none,
            // because several vouchers can share a creation timestamp - and a page boundary
            // that skips a row is a customer's fuel missing from their wallet.
            .ThenBy(v => v.Id)
            .Skip(command.Paging.Skip)
            .Take(command.Paging.PageSize)
            .ToListAsync(cancellationToken);

        var vouchers = entities.Select(v =>
        {
            // The row stays in the list for whoever bought it - the owner's hub reads this same
            // list to show what each order delivered and what is still undistributed - but the
            // redemption material goes only to the current holder. After issuing fuel to a worker
            // the owner still matches AssignedToUserId, and without this the owner received the
            // payload and a rendered PNG for fuel they had already given away: the same bearer
            // instrument that GET /vouchers/{id}/qr refuses them with 403, and that
            // MarkVoucherAsUsed refuses them on.
            var isHolder = VoucherHolder.CanRedeem(v.WorkerUserId, v.AssignedToUserId, command.UserId);

            // Rendered only for the holder: generating the PNG is the expensive half of this
            // handler, and a non-holder has no use for the image either.
            var qrImage = isHolder ? GenerateQrImage(v) : null;
            return new VoucherDto(
                v.Id,
                v.Brand,
                v.FuelTypeId.ToString(),
                v.Liters,
                v.Liters,
                v.CustomerExpirationDate,
                v.VoucherNumber,
                v.VoucherNumber,
                isHolder ? v.QrPayload : string.Empty,
                isHolder ? v.QrPayload : string.Empty,
                v.Status.ToString(),
                v.WorkerUserId == command.UserId ? "gifted" : "own",
                v.LegalEntityId,
                v.WorkerUserId,
                v.WorkerUser?.FirstName,
                v.WorkerUser?.LastName,
                qrImage,
                v.CreatedAtUtc,
                v.UpdatedAtUtc,
                // Human-readable fuel name from the catalog (e.g. "ДП ЄВРО"); the mobile UI
                // falls back to the raw FuelTypeId when it is missing, which rendered as
                // "OKKO-DP" on the voucher cards.
                v.FuelType?.Name ?? v.FuelTypeId.ToString()
            );
        }).ToList();

        var hasNextPage = totalCount > command.Paging.Skip + vouchers.Count;
        return new GetUserVouchersResponse(vouchers, totalCount, hasNextPage);
    }

    private string? GenerateQrImage(FuelVoucher voucher)
    {
        if (string.IsNullOrWhiteSpace(voucher.QrPayload))
        {
            _logger.LogWarning(
                "Voucher {VoucherId} ({VoucherNumber}) has no QR payload — cannot generate QR image",
                voucher.Id, voucher.VoucherNumber);
            return null;
        }

        if (voucher.QrParameters is null)
        {
            _logger.LogWarning(
                "Voucher {VoucherId} ({VoucherNumber}) has no stored QR parameters — generated QR may differ from original",
                voucher.Id, voucher.VoucherNumber);
        }

        return "data:image/png;base64," + _qrGenerator.GenerateQrCode(
            voucher.QrPayload,
            eccLevel: voucher.QrParameters?.EccLevel,
            version: voucher.QrParameters?.Version,
            encodingMode: voucher.QrParameters?.EncodingMode,
            maskPattern: voucher.QrParameters?.MaskPattern);
    }
}

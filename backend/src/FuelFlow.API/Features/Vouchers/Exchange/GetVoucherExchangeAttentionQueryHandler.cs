using FuelFlow.Features.Settings;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.Exchange;

/// <summary>
/// Lists operator STOCK vouchers that are already expired or lapsing soon (≤ the renewal threshold,
/// default 14 days) so staff can renew them with the provider. Stock-only: assigned / worker-held /
/// deleted vouchers are excluded. Drives both the "Заміна талонів" attention list and the nav badge
/// count (planning #104).
/// </summary>
public sealed class GetVoucherExchangeAttentionQueryHandler
{
    private readonly ApplicationDbContext _context;
    private readonly RuntimeSettingsService _settings;

    public GetVoucherExchangeAttentionQueryHandler(ApplicationDbContext context, RuntimeSettingsService settings)
    {
        _context = context;
        _settings = settings;
    }

    public async Task<VoucherExchangeAttentionResponse> HandleAsync(CancellationToken ct = default)
    {
        var threshold = await _settings.GetVoucherRenewalThresholdDaysAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var cutoff = today.AddDays(threshold);

        var items = await Filter(cutoff)
            .OrderBy(v => v.ProviderExpirationDate)
            .Select(v => new VoucherExchangeAttentionItem
            {
                Id = v.Id,
                VoucherNumber = v.VoucherNumber,
                Provider = v.Provider,
                FuelTypeId = v.FuelTypeId,
                FuelName = v.FuelType != null ? v.FuelType.Name : null,
                Liters = v.Liters,
                ExpirationDate = v.ProviderExpirationDate,
                Status = v.Status.ToString(),
                // True when this voucher reached us because a customer bought past its real term and we
                // issued them a different one. Those are the ones owed to the supplier with a surcharge, so
                // the operator can tell "our stock aged out" from "we handed this one back".
                ReleasedFromCustomer = _context.VoucherRenewalItems.Any(i => i.SourceVoucherId == v.Id)
            })
            .ToListAsync(ct);

        foreach (var item in items)
            item.DaysLeft = item.ExpirationDate.DayNumber - today.DayNumber;

        return new VoucherExchangeAttentionResponse
        {
            Data = items,
            ThresholdDays = threshold,
            Providers = items.Select(i => i.Provider).Distinct().OrderBy(p => p).ToList(),
            Fuels = items.Where(i => i.FuelName != null).Select(i => i.FuelName!).Distinct().OrderBy(f => f).ToList()
        };
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        var threshold = await _settings.GetVoucherRenewalThresholdDaysAsync(ct);
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(threshold);
        return await Filter(cutoff).CountAsync(ct);
    }

    // Stock-only, already-expired OR (available/imported and lapsing within the threshold). Query
    // filters are ignored so soft-deleted stock is still excluded explicitly via !IsDeleted.
    // Vouchers already retired through a confirmed exchange are excluded too: a voucher_exchanges row
    // means the operator finished renewing it with the supplier, so it must drop off both the list and
    // the badge (planning #183). Leaving it in only ever yields a dead-end row — re-confirming it is
    // rejected by the idempotency guard as "already exchanged", so the badge never clears.
    private IQueryable<FuelVoucher> Filter(DateOnly cutoff)
        => _context.FuelVouchers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(v => !v.IsDeleted
                && v.AssignedToUserId == null
                && v.WorkerUserId == null
                && !_context.VoucherExchanges.Any(e => e.OldVoucherId == v.Id)
                && (v.Status == VoucherStatus.Expired
                    || ((v.Status == VoucherStatus.Available || v.Status == VoucherStatus.Imported)
                        && v.ProviderExpirationDate <= cutoff)));
}

public sealed class VoucherExchangeAttentionResponse
{
    public List<VoucherExchangeAttentionItem> Data { get; set; } = new();
    public int ThresholdDays { get; set; }
    public List<string> Providers { get; set; } = new();
    public List<string> Fuels { get; set; } = new();
}

public sealed class VoucherExchangeAttentionItem
{
    public Guid Id { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string FuelTypeId { get; set; } = string.Empty;
    public string? FuelName { get; set; }
    public decimal Liters { get; set; }
    public DateOnly ExpirationDate { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>Days until expiry; negative = already expired.</summary>
    public int DaysLeft { get; set; }

    /// <summary>
    /// The voucher was unlinked from a customer by a renewal replacement, so it is owed to the supplier
    /// with a surcharge rather than being stock that simply aged out.
    /// </summary>
    public bool ReleasedFromCustomer { get; set; }
}

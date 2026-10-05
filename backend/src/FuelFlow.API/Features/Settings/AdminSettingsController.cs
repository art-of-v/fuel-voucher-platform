using System.Globalization;
using FuelFlow.Features.Settings.SharedModels;
using FuelFlow.Features.Vouchers.Renewal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Settings;

[ApiController]
[Route("api/admin/settings")]
[Authorize(Policy = "Staff")]
public sealed class AdminSettingsController : ControllerBase
{
    private readonly RuntimeSettingsService _settings;
    private readonly ApplicationDbContext _context;

    public AdminSettingsController(RuntimeSettingsService settings, ApplicationDbContext context)
    {
        _settings = settings;
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var enabled = await _settings.IsAutoRefundEnabledAsync(cancellationToken);
        var delayDays = await _settings.GetAutoRefundDelayDaysAsync(cancellationToken);

        var cleanupEnabled = await _settings.IsOrderCleanupEnabledAsync(cancellationToken);
        var cleanupRetentionDays = await _settings.GetOrderCleanupRetentionDaysAsync(cancellationToken);

        var dataRetentionEnabled = await _settings.IsDataRetentionEnabledAsync(cancellationToken);

        var expiredVoucherLossEnabled = await _settings.IsExpiredVoucherLossEnabledAsync(cancellationToken);

        var renewal = await _settings.GetVoucherRenewalConfigAsync(cancellationToken);
        var termSale = await _settings.GetVoucherTermConfigAsync(cancellationToken);

        return Ok(new SettingsDto
        {
            AutoRefund = new AutoRefundSettingsDto
            {
                Enabled = enabled,
                DelayDays = delayDays
            },
            OrderCleanup = new OrderCleanupSettingsDto
            {
                Enabled = cleanupEnabled,
                RetentionDays = cleanupRetentionDays
            },
            DataRetention = new DataRetentionSettingsDto
            {
                Enabled = dataRetentionEnabled
            },
            ExpiredVoucherLoss = new ExpiredVoucherLossSettingsDto
            {
                Enabled = expiredVoucherLossEnabled
            },
            VoucherRenewal = new VoucherRenewalSettingsDto
            {
                Enabled = renewal.Enabled,
                TriggerThresholdDays = renewal.TriggerThresholdDays,
                Tiers = renewal.Tiers
                    .Select(t => new VoucherRenewalTierSettingsDto
                    {
                        Term = t.Term.Code(),
                        Enabled = t.Enabled,
                        RatePerLiterUah = t.RatePerLiterUah,
                        Offerable = t.IsOfferable
                    })
                    .ToList()
            },
            VoucherTerm = new VoucherTermSettingsDto
            {
                Enabled = termSale.Enabled,
                Tiers = termSale.Tiers
                    .Select(t => new VoucherTermTierSettingsDto
                    {
                        Term = t.Term.Code(),
                        Enabled = t.Enabled,
                        DiscountPerLiterUah = t.DiscountPerLiterUah,
                        Offerable = t.IsOfferable
                    })
                    .ToList(),
                // What each fuel can actually carry. The ladder is one global setting while margins are per
                // fuel, so a discount that is generous on a high-margin fuel can be unsellable on a low-margin
                // one — and checkout refuses below cost, which reads as a broken picker rather than a pricing
                // fact. Reported here so the numbers cannot drift from the catalog.
                MarginFloorUah = TermMarginFloorUah,
                Fuels = (await TermMarginsAsync(cancellationToken))
                    .OrderBy(f => f.MarginPerLiterUah)
                    .ToList()
            }
        });
    }

    /// <summary>
    /// The margin a term discount must leave behind, in UAH per litre.
    /// </summary>
    /// <remarks>
    /// A policy rather than a cost rule: the below-cost refusal already stops a losing sale, but a sale that
    /// earns 10 kopecks is not worth the support it generates. Expressed in UAH per litre because that is the
    /// unit the ladder is priced in.
    /// </remarks>
    public const decimal TermMarginFloorUah = 0.5m;

    /// <summary>
    /// The thinnest margin each fuel has across its packages.
    /// </summary>
    /// <remarks>
    /// Thinnest, not average: a discount is a per-litre figure, so the package with the least room decides
    /// whether the whole fuel can carry it. Averages would hide the fuel that quietly cannot be sold.
    /// Packages with no cost recorded are skipped rather than treated as free stock — an unknown cost is not
    /// evidence of margin, and reporting it as infinite margin would hide the problem instead.
    /// </remarks>
    private async Task<List<VoucherTermFuelMarginDto>> TermMarginsAsync(CancellationToken cancellationToken)
    {
        // Joined to the fuel table explicitly rather than through a navigation: FuelPackage has no
        // FuelType navigation to walk in a projection, and the fuel's station is what makes a name
        // readable when several stations sell the same grade.
        return await _context.FuelPackages
            .AsNoTracking()
            .Join(_context.FuelTypes,
                package => package.FuelTypeId,
                fuel => fuel.Id,
                (package, fuel) => new { package, fuel.Name, fuel.StationId })
            .Where(row => row.package.SupplierPricePerLiter != null && row.package.FinalPricePerLiter != null)
            .GroupBy(row => new { row.package.FuelTypeId, row.Name, row.StationId })
            .Select(g => new VoucherTermFuelMarginDto
            {
                FuelTypeId = g.Key.FuelTypeId,
                Name = g.Key.Name,
                StationId = g.Key.StationId,
                MarginPerLiterUah = g.Min(row =>
                    row.package.FinalPricePerLiter!.Value - row.package.SupplierPricePerLiter!.Value)
            })
            .ToListAsync(cancellationToken);
    }

    [HttpPut]
    public async Task<IActionResult> Update(
        [FromBody] UpdateSettingsRequest request,
        CancellationToken cancellationToken)
    {
        if (request?.AutoRefund is null && request?.OrderCleanup is null
            && request?.DataRetention is null && request?.ExpiredVoucherLoss is null
            && request?.VoucherRenewal is null
            && request?.VoucherTerm is null)
        {
            return BadRequest(new { success = false, error = "No settings supplied" });
        }

        object? autoRefundResult = null;
        object? orderCleanupResult = null;
        object? dataRetentionResult = null;
        object? expiredVoucherLossResult = null;
        object? voucherRenewalResult = null;
        object? voucherTermResult = null;

        if (request.AutoRefund is not null)
        {
            await _settings.UpsertAsync(
                AppSettingKeys.AutoRefundEnabled,
                request.AutoRefund.Enabled.ToString(),
                GetUserId(),
                GetUserName(),
                cancellationToken);

            var delayDays = Math.Max(1, request.AutoRefund.DelayDays);
            await _settings.UpsertAsync(
                AppSettingKeys.AutoRefundDelayDays,
                delayDays.ToString(),
                GetUserId(),
                GetUserName(),
                cancellationToken);

            autoRefundResult = new { enabled = request.AutoRefund.Enabled, delayDays };
        }

        if (request.OrderCleanup is not null)
        {
            await _settings.UpsertAsync(
                AppSettingKeys.OrderCleanupEnabled,
                request.OrderCleanup.Enabled.ToString(),
                GetUserId(),
                GetUserName(),
                cancellationToken);

            // Clamp to a whole day minimum so a stray 0/negative can never make every abandoned
            // order instantly purgeable; mirrors the job's own Math.Max(1, ...) guard.
            var retentionDays = Math.Max(1, request.OrderCleanup.RetentionDays);
            await _settings.UpsertAsync(
                AppSettingKeys.OrderCleanupRetentionDays,
                retentionDays.ToString(),
                GetUserId(),
                GetUserName(),
                cancellationToken);

            orderCleanupResult = new { enabled = request.OrderCleanup.Enabled, retentionDays };
        }

        if (request.DataRetention is not null)
        {
            await _settings.UpsertAsync(
                AppSettingKeys.DataRetentionEnabled,
                request.DataRetention.Enabled.ToString(),
                GetUserId(),
                GetUserName(),
                cancellationToken);

            dataRetentionResult = new { enabled = request.DataRetention.Enabled };
        }

        if (request.ExpiredVoucherLoss is not null)
        {
            await _settings.UpsertAsync(
                AppSettingKeys.ExpiredVoucherLossEnabled,
                request.ExpiredVoucherLoss.Enabled.ToString(),
                GetUserId(),
                GetUserName(),
                cancellationToken);

            expiredVoucherLossResult = new { enabled = request.ExpiredVoucherLoss.Enabled };
        }

        if (request.VoucherRenewal is not null)
        {
            await _settings.UpsertAsync(
                AppSettingKeys.VoucherRenewalEnabled,
                request.VoucherRenewal.Enabled.ToString(),
                GetUserId(),
                GetUserName(),
                cancellationToken);

            // Clamp the trigger window to a sane whole-day range: at least 1 day (a 0/negative window
            // would never surface the button) and at most a year (a runaway value would offer renewal
            // on vouchers that are nowhere near expiry).
            var thresholdDays = Math.Clamp(request.VoucherRenewal.TriggerThresholdDays, 1, 365);
            await _settings.UpsertAsync(
                AppSettingKeys.VoucherRenewalTriggerThresholdDays,
                thresholdDays.ToString(),
                GetUserId(),
                GetUserName(),
                cancellationToken);

            var tierResults = new List<object>();
            foreach (var tier in request.VoucherRenewal.Tiers ?? new List<VoucherRenewalTierSettingsDto>())
            {
                // Ignore unknown/garbage tier codes rather than persisting orphan keys.
                if (!VoucherRenewalTerms.TryFromCode(tier.Term, out var term))
                {
                    continue;
                }

                var code = term.Code();

                await _settings.UpsertAsync(
                    AppSettingKeys.VoucherRenewalTierEnabled(code),
                    tier.Enabled.ToString(),
                    GetUserId(),
                    GetUserName(),
                    cancellationToken);

                // A price can never be negative; a zero rate is allowed but leaves the tier not-offerable
                // (IsOfferable requires rate > 0), so an admin can pre-enable a tier before pricing it.
                var rate = Math.Max(0m, tier.RatePerLiterUah);
                await _settings.UpsertAsync(
                    AppSettingKeys.VoucherRenewalTierRatePerLiter(code),
                    rate.ToString(CultureInfo.InvariantCulture),
                    GetUserId(),
                    GetUserName(),
                    cancellationToken);

                tierResults.Add(new { term = code, enabled = tier.Enabled, ratePerLiterUah = rate });
            }

            voucherRenewalResult = new
            {
                enabled = request.VoucherRenewal.Enabled,
                triggerThresholdDays = thresholdDays,
                tiers = tierResults
            };
        }

        if (request.VoucherTerm is not null)
        {
            await _settings.UpsertAsync(
                AppSettingKeys.VoucherTermSaleEnabled,
                request.VoucherTerm.Enabled.ToString(),
                GetUserId(),
                GetUserName(),
                cancellationToken);

            var termTierResults = new List<object>();
            foreach (var tier in request.VoucherTerm.Tiers ?? new List<VoucherTermTierSettingsDto>())
            {
                // Ignore unknown/garbage tier codes rather than persisting orphan keys.
                if (!VoucherRenewalTerms.TryFromCode(tier.Term, out var term))
                {
                    continue;
                }

                var code = term.Code();

                await _settings.UpsertAsync(
                    AppSettingKeys.VoucherTermTierEnabled(code),
                    tier.Enabled.ToString(),
                    GetUserId(),
                    GetUserName(),
                    cancellationToken);

                // A discount can never be negative; a zero discount is allowed but leaves the tier
                // unsellable (IsOfferable requires discount > 0), so a manager can pre-enable a term
                // before pricing it. Capped at the litre price by the checkout guard, so an absurd value
                // here can never sell fuel below cost.
                var discount = Math.Clamp(tier.DiscountPerLiterUah, 0m, 1_000_000m);
                await _settings.UpsertAsync(
                    AppSettingKeys.VoucherTermTierDiscountPerLiter(code),
                    discount.ToString(CultureInfo.InvariantCulture),
                    GetUserId(),
                    GetUserName(),
                    cancellationToken);

                termTierResults.Add(new { term = code, enabled = tier.Enabled, discountPerLiterUah = discount });
            }

            voucherTermResult = new
            {
                enabled = request.VoucherTerm.Enabled,
                tiers = termTierResults
            };
        }

        return Ok(new
        {
            success = true,
            autoRefund = autoRefundResult,
            orderCleanup = orderCleanupResult,
            dataRetention = dataRetentionResult,
            expiredVoucherLoss = expiredVoucherLossResult,
            voucherRenewal = voucherRenewalResult,
            voucherTerm = voucherTermResult
        });
    }

    private Guid? GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(claim, out var parsed) ? parsed : null;
    }

    private string? GetUserName()
    {
        var first = User.FindFirst("first_name")?.Value;
        var last = User.FindFirst("last_name")?.Value;
        if (!string.IsNullOrWhiteSpace(first) || !string.IsNullOrWhiteSpace(last))
        {
            return $"{first} {last}".Trim();
        }
        return User.FindFirst(ClaimTypes.Name)?.Value;
    }
}

public sealed class SettingsDto
{
    public AutoRefundSettingsDto AutoRefund { get; set; } = new();
    public OrderCleanupSettingsDto OrderCleanup { get; set; } = new();
    public DataRetentionSettingsDto DataRetention { get; set; } = new();
    public ExpiredVoucherLossSettingsDto ExpiredVoucherLoss { get; set; } = new();
    public VoucherRenewalSettingsDto VoucherRenewal { get; set; } = new();
    public VoucherTermSettingsDto VoucherTerm { get; set; } = new();
}

public sealed class AutoRefundSettingsDto
{
    public bool Enabled { get; set; }
    public int DelayDays { get; set; }
}

public sealed class OrderCleanupSettingsDto
{
    public bool Enabled { get; set; }
    public int RetentionDays { get; set; }
}

public sealed class DataRetentionSettingsDto
{
    public bool Enabled { get; set; }
}

public sealed class ExpiredVoucherLossSettingsDto
{
    public bool Enabled { get; set; }
}

public sealed class VoucherRenewalSettingsDto
{
    public bool Enabled { get; set; }
    public int TriggerThresholdDays { get; set; }
    public List<VoucherRenewalTierSettingsDto> Tiers { get; set; } = new();
}

public sealed class VoucherRenewalTierSettingsDto
{
    /// <summary>Stable tier code (e.g. "1w", "3m") — see <see cref="VoucherRenewalTerms"/>.</summary>
    public string Term { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public decimal RatePerLiterUah { get; set; }
    /// <summary>Read-only echo of whether the tier is actually sellable (enabled AND priced). Ignored on write.</summary>
    public bool Offerable { get; set; }
}

public sealed class VoucherTermSettingsDto
{
    public bool Enabled { get; set; }
    public List<VoucherTermTierSettingsDto> Tiers { get; set; } = new();

    /// <summary>Margin a discount must leave per litre, in UAH. See TermMarginFloorUah.</summary>
    public decimal MarginFloorUah { get; set; }

    /// <summary>Per-fuel margins, thinnest first. Ignored on write.</summary>
    public List<VoucherTermFuelMarginDto> Fuels { get; set; } = new();
}

/// <summary>
/// What a fuel's thinnest margin allows, so a manager sees a discount priced against reality.
/// </summary>
public sealed class VoucherTermFuelMarginDto
{
    public string FuelTypeId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string StationId { get; set; } = string.Empty;
    /// <summary>Least margin across this fuel's packages, in UAH per litre. Zero means it has none.</summary>
    public decimal MarginPerLiterUah { get; set; }
}

public sealed class VoucherTermTierSettingsDto
{
    /// <summary>Stable tier code (e.g. "1w", "3m") - see <see cref="VoucherRenewalTerms"/>.</summary>
    public string Term { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    /// <summary>UAH off the per-litre price for this term. Bigger for a shorter term.</summary>
    public decimal DiscountPerLiterUah { get; set; }
    /// <summary>Read-only echo of whether the tier is actually sellable (enabled AND priced). Ignored on write.</summary>
    public bool Offerable { get; set; }
}

public sealed class UpdateSettingsRequest
{
    public VoucherTermSettingsDto? VoucherTerm { get; set; }
    public AutoRefundSettingsDto? AutoRefund { get; set; }
    public OrderCleanupSettingsDto? OrderCleanup { get; set; }
    public DataRetentionSettingsDto? DataRetention { get; set; }
    public ExpiredVoucherLossSettingsDto? ExpiredVoucherLoss { get; set; }
    public VoucherRenewalSettingsDto? VoucherRenewal { get; set; }
}

using System.Globalization;
using System.Text.Json;
using FuelFlow.Features.Pricing.Terms.SharedModels;
using FuelFlow.Features.Settings.SharedModels;
using FuelFlow.Features.Feedback;
using FuelFlow.Features.Vouchers.Renewal;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using FuelFlow.Persistence;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Settings;

[ApiController]
[Route("api/admin/settings")]
[Authorize(Policy = "Staff")]
public sealed class AdminSettingsController : ControllerBase
{
    private readonly RuntimeSettingsService _settings;
    private readonly ApplicationDbContext _context;
    private readonly ITelegramFeedbackClient _feedbackBot;
    private readonly TelegramFeedbackOptions _feedbackOptions;

    public AdminSettingsController(
        RuntimeSettingsService settings,
        ApplicationDbContext context,
        ITelegramFeedbackClient feedbackBot,
        IOptions<TelegramFeedbackOptions> feedbackOptions)
    {
        _settings = settings;
        _context = context;
        _feedbackBot = feedbackBot;
        _feedbackOptions = feedbackOptions.Value;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var enabled = await _settings.IsAutoRefundEnabledAsync(cancellationToken);
        var delayDays = await _settings.GetAutoRefundDelayDaysAsync(cancellationToken);

        var cleanupEnabled = await _settings.IsDeletedUnpaidOrderCleanupEnabledAsync(cancellationToken);
        var cleanupRetentionDays = await _settings.GetDeletedUnpaidOrderCleanupRetentionDaysAsync(cancellationToken);

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
            DeletedUnpaidOrderCleanup = new DeletedUnpaidOrderCleanupSettingsDto
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
            FeedbackBot = new FeedbackBotSettingsDto
            {
                Enabled = await _settings.IsFeedbackBotEnabledAsync(cancellationToken),
                TelegramConfigured = _feedbackOptions.IsConfigured,
                Webhook = await ReadWebhookStatusAsync(cancellationToken)
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
        // This reads the FUEL, not the volume: the margin shown against a term ladder is a
        // per-litre figure, and it exists once per fuel. There is nothing to group over any more -
        // the `Min(...)` across pools existed because two pools of one fuel could be priced apart,
        // and now they cannot.
        //
        // Materialised before the projection, because the customer price is not a column any more: it
        // is `min(cost + profit, pump - minDiscount)` over four nullable columns, which SQL cannot
        // express through the one function that owns it. The row count is one per fuel - 25 on
        // production - so this is a list, not a scan.
        var priced = (await _context.FuelTypes
                .AsNoTracking()
                .ToListAsync(cancellationToken))
            .Where(fuel => fuel.SupplierPricePerLiter is not null)
            .Select(fuel => new
            {
                FuelTypeId = fuel.Id,
                fuel.Name,
                fuel.BrandId,
                Supplier = fuel.SupplierPricePerLiter!.Value,
                Final = fuel.FinalPerLiter()
            })
            .Where(row => row.Final is not null)
            .ToList();

        return priced
            .Select(row => new VoucherTermFuelMarginDto
            {
FuelTypeId = row.FuelTypeId,
                Name = row.Name,
                BrandId = row.BrandId.ToString(),
                // `final - cost` off the one row that holds both figures, which is now the only row
                // there is. When two pools could disagree this was a min() across them; with one
                // price per grade there is nothing to choose between.
                MarginPerLiterUah = row.Final!.Value - row.Supplier
            })
            .ToList();
    }

    [HttpPut]
    public async Task<IActionResult> Update(
        [FromBody] UpdateSettingsRequest request,
        CancellationToken cancellationToken)
    {
        if (request?.AutoRefund is null && request?.DeletedUnpaidOrderCleanup is null
            && request?.DataRetention is null && request?.ExpiredVoucherLoss is null
            && request?.VoucherRenewal is null
            && request?.VoucherTerm is null
            && request?.FeedbackBot is null)
        {
            return BadRequest(new { success = false, error = "No settings supplied" });
        }

        object? autoRefundResult = null;
        object? deletedUnpaidOrderCleanupResult = null;
        object? dataRetentionResult = null;
        object? expiredVoucherLossResult = null;
        object? voucherRenewalResult = null;
        object? voucherTermResult = null;
        object? feedbackBotResult = null;

        if (request.FeedbackBot is not null)
        {
            // The toggle is honoured even while Telegram is unprovisioned, so a manager can stage
            // the decision before the env change. The app will simply keep hiding the tab until
            // both halves are true - the deep-link endpoint checks IsConfigured as well.
            await _settings.UpsertAsync(
                AppSettingKeys.FeedbackBotEnabled,
                request.FeedbackBot.Enabled.ToString(),
                GetUserId(),
                GetUserName(),
                cancellationToken);

            feedbackBotResult = new { enabled = request.FeedbackBot.Enabled };
        }

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

        if (request.DeletedUnpaidOrderCleanup is not null)
        {
            await _settings.UpsertAsync(
                AppSettingKeys.DeletedUnpaidOrderCleanupEnabled,
                request.DeletedUnpaidOrderCleanup.Enabled.ToString(),
                GetUserId(),
                GetUserName(),
                cancellationToken);

            // Clamp to a whole day minimum so a stray 0/negative can never make every abandoned
            // order instantly purgeable; mirrors the job's own Math.Max(1, ...) guard.
            var retentionDays = Math.Max(1, request.DeletedUnpaidOrderCleanup.RetentionDays);
            await _settings.UpsertAsync(
                AppSettingKeys.DeletedUnpaidOrderCleanupRetentionDays,
                retentionDays.ToString(),
                GetUserId(),
                GetUserName(),
                cancellationToken);

            deletedUnpaidOrderCleanupResult = new { enabled = request.DeletedUnpaidOrderCleanup.Enabled, retentionDays };
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
                await UpsertTierAsync(
                    VoucherTermPricingSchemes.RenewalRate,
                    code,
                    tier.Enabled,
                    rate,
                    cancellationToken);

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
                await UpsertTierAsync(
                    VoucherTermPricingSchemes.PurchaseTermDiscount,
                    code,
                    tier.Enabled,
                    discount,
                    cancellationToken);

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
            deletedUnpaidOrderCleanup = deletedUnpaidOrderCleanupResult,
            dataRetention = dataRetentionResult,
            expiredVoucherLoss = expiredVoucherLossResult,
            voucherRenewal = voucherRenewalResult,
            voucherTerm = voucherTermResult,
            feedbackBot = feedbackBotResult
        });
    }

    /// <summary>
    /// Records one tier change in <c>voucher_term_pricing</c>, closing the row it replaces.
    /// </summary>
    /// <remarks>
    /// The key/value row is written too, by the caller. That is transitional and deliberate: it is
    /// the fallback the loader uses for a tier that has no table row yet, and keeping it in step means
    /// removing the fallback later cannot change a price. Once every tier is written here, the
    /// <c>UpsertAsync</c> calls for the 32 tier keys are dead and can go in one follow-up.
    /// </remarks>
    private Task UpsertTierAsync(
        string scheme,
        string termCode,
        bool enabled,
        decimal ratePerLiter,
        CancellationToken cancellationToken)
        => _settings.SetTierAsync(
            scheme,
            termCode,
            enabled,
            ratePerLiter,
            GetUserId(),
            GetUserName(),
            cancellationToken);

    /// <summary>
    /// Registers the feedback webhook against this deployment's own public address, so an operator
    /// does not have to hand-assemble the URL and paste it into a terminal. The public origin is
    /// read off the incoming request rather than configured, because it differs per environment
    /// (localhost tunnel in development, the API domain behind the proxy in production) and a
    /// misconfigured value would point Telegram at a host that answers 404 for every update.
    /// </summary>
    [HttpPost("feedback/webhook")]
    public async Task<IActionResult> RegisterFeedbackWebhook(CancellationToken cancellationToken)
    {
        if (!_feedbackOptions.IsConfigured)
        {
            return BadRequest(new
            {
                success = false,
                error = "Telegram:Feedback is not configured. Set the bot token, staff chat, webhook " +
                        "secret and signing key, then restart the API."
            });
        }

        var url = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/api/telegram/feedback/webhook";

        try
        {
            await _feedbackBot.SetWebhookAsync(url, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
        {
            // Telegram's own error text is the useful part ("Bad webhook: ..."), so it reaches the
            // admin screen verbatim instead of being flattened into a 500.
            return BadRequest(new { success = false, error = ex.Message });
        }

        return Ok(new { success = true, url, webhook = await ReadWebhookStatusAsync(cancellationToken) });
    }

    /// <summary>
    /// Current webhook state for the settings screen, or null when Telegram cannot be reached or the
    /// feature is unprovisioned. Null rather than a fabricated "not registered": an unreadable
    /// status and a confirmed-unregistered webhook are different situations and an operator must be
    /// able to tell them apart.
    /// </summary>
    private async Task<FeedbackWebhookStatusDto?> ReadWebhookStatusAsync(CancellationToken cancellationToken)
    {
        if (!_feedbackOptions.IsConfigured)
        {
            return null;
        }

        try
        {
            var status = await _feedbackBot.GetWebhookStatusAsync(cancellationToken);

            return new FeedbackWebhookStatusDto
            {
                Url = status.Url,
                Registered = !string.IsNullOrWhiteSpace(status.Url),
                PendingUpdateCount = status.PendingUpdateCount,
                LastErrorMessage = status.LastErrorMessage,
                LastErrorDate = status.LastErrorDate
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new FeedbackWebhookStatusDto { LastErrorMessage = ex.Message };
        }
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
    public DeletedUnpaidOrderCleanupSettingsDto DeletedUnpaidOrderCleanup { get; set; } = new();
    public DataRetentionSettingsDto DataRetention { get; set; } = new();
    public ExpiredVoucherLossSettingsDto ExpiredVoucherLoss { get; set; } = new();
    public VoucherRenewalSettingsDto VoucherRenewal { get; set; } = new();
    public VoucherTermSettingsDto VoucherTerm { get; set; } = new();
    public FeedbackBotSettingsDto FeedbackBot { get; set; } = new();
}

public sealed class AutoRefundSettingsDto
{
    public bool Enabled { get; set; }
    public int DelayDays { get; set; }
}

public sealed class DeletedUnpaidOrderCleanupSettingsDto
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

/// <summary>
/// The in-app feedback channel: the runtime toggle plus whether the Telegram integration behind it is
/// actually provisioned. Both halves are reported because a toggle that reads "on" while
/// <c>Configured</c> is false would send a manager to debug a bot that was never created.
/// </summary>
public sealed class FeedbackBotSettingsDto
{
    public bool Enabled { get; set; }

    /// <summary>
    /// Read-only: whether bot token, staff chat and webhook secret are all present in configuration.
    /// Turning this on is an env change plus a restart, which is why it is reported and not editable.
    /// </summary>
    public bool TelegramConfigured { get; set; }

    /// <summary>Read-only echo of Telegram's own view of the webhook. Null when never checked.</summary>
    public FeedbackWebhookStatusDto? Webhook { get; set; }
}

/// <summary>What Telegram reports about the registered feedback webhook, or the error explaining why not.</summary>
public sealed class FeedbackWebhookStatusDto
{
    public string Url { get; set; } = string.Empty;
    public bool Registered { get; set; }
    public int PendingUpdateCount { get; set; }
    public string? LastErrorMessage { get; set; }
    public string? LastErrorDate { get; set; }
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
    public Guid FuelTypeId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The brand's uuid, rendered as a string. This DTO is settings-shaped and the admin form echoes
    /// it back verbatim, so the wire type stays a string; the value it now carries is an id rather
    /// than the network's code, and the form must send it back unchanged.
    /// </summary>
    public string BrandId { get; set; } = string.Empty;
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
    public DeletedUnpaidOrderCleanupSettingsDto? DeletedUnpaidOrderCleanup { get; set; }
    public DataRetentionSettingsDto? DataRetention { get; set; }
    public ExpiredVoucherLossSettingsDto? ExpiredVoucherLoss { get; set; }
    public VoucherRenewalSettingsDto? VoucherRenewal { get; set; }
    public FeedbackBotSettingsDto? FeedbackBot { get; set; }
}

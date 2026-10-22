using FuelFlow.Features.Vouchers.Renewal;

namespace FuelFlow.Features.Settings;

/// <summary>
/// The whole admin settings screen in one shape. The property names are the wire names: the admin
/// SPA binds to them, so they are camelCased by the JSON policy and nothing else.
/// </summary>
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

    /// <summary>Margin a discount must leave per litre, in UAH. See <see cref="AdminSettingsPolicy.TermMarginFloorUah"/>.</summary>
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

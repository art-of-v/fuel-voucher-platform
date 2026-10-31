using FuelFlow.Features.Settings;

namespace FuelFlow.Features.Settings.UpdateAdminSettings;

/// <summary>
/// A settings write as the controller received it: the sections the admin screen sent, and who sent
/// them. The actor is a plain value here - the handler never sees a claims principal, and the
/// controller is the only place that reads one.
/// </summary>
public sealed record UpdateAdminSettingsCommand(
    UpdateSettingsRequest Request,
    Guid? ActorUserId,
    string? ActorUserName);

/// <summary>
/// The answer of a settings write: what every section became. A section the request did not touch
/// stays null, which is what lets a partial PUT answer for the sections it changed and say nothing
/// about the ones it did not.
/// </summary>
public sealed record UpdateAdminSettingsResult(
    bool Success,
    string? Error,
    AutoRefundSettingsUpdate? AutoRefund,
    DeletedUnpaidOrderCleanupSettingsUpdate? DeletedUnpaidOrderCleanup,
    DataRetentionSettingsUpdate? DataRetention,
    ExpiredVoucherLossSettingsUpdate? ExpiredVoucherLoss,
    VoucherRenewalSettingsUpdate? VoucherRenewal,
    VoucherTermSettingsUpdate? VoucherTerm,
    FeedbackBotSettingsUpdate? FeedbackBot)
{
    /// <summary>
    /// The stable machine code of the refusal, when there was one. The admin renders its own
    /// sentence from this and shows <see cref="Error"/> only for a code it has not learnt.
    /// </summary>
    public string? Code { get; init; }

    /// <summary>The refusal for a request that carries no section at all.</summary>
    public static UpdateAdminSettingsResult NoSettingsSupplied()
        => new(false, "No settings supplied", null, null, null, null, null, null, null)
        {
            Code = "settings_none_supplied"
        };
}

/// <summary>What the auto-refund switch became.</summary>
public sealed record AutoRefundSettingsUpdate(bool Enabled, int DelayDays);

/// <summary>What the abandoned-order cleanup switch became.</summary>
public sealed record DeletedUnpaidOrderCleanupSettingsUpdate(bool Enabled, int RetentionDays);

/// <summary>What the data-retention switch became.</summary>
public sealed record DataRetentionSettingsUpdate(bool Enabled);

/// <summary>What the expired-voucher loss switch became.</summary>
public sealed record ExpiredVoucherLossSettingsUpdate(bool Enabled);

/// <summary>What the feedback channel switch became.</summary>
public sealed record FeedbackBotSettingsUpdate(bool Enabled);

/// <summary>What one renewal tier became.</summary>
public sealed record VoucherRenewalTierUpdate(string Term, bool Enabled, decimal RatePerLiterUah);

/// <summary>What the renewal ladder became.</summary>
public sealed record VoucherRenewalSettingsUpdate(
    bool Enabled,
    int TriggerThresholdDays,
    IReadOnlyList<VoucherRenewalTierUpdate> Tiers);

/// <summary>What one purchase term became.</summary>
public sealed record VoucherTermTierUpdate(string Term, bool Enabled, decimal DiscountPerLiterUah);

/// <summary>What the purchase-term ladder became.</summary>
public sealed record VoucherTermSettingsUpdate(
    bool Enabled,
    IReadOnlyList<VoucherTermTierUpdate> Tiers);

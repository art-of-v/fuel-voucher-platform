using System.Globalization;
using FuelFlow.Features.Pricing.Terms.SharedModels;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Settings.SharedModels;
using FuelFlow.Features.Vouchers.Renewal;

namespace FuelFlow.Features.Settings.UpdateAdminSettings;

/// <summary>
/// Writes the admin settings: the runtime switches and the two voucher ladders.
/// </summary>
/// <remarks>
/// The clamps live here rather than in the controller, because they are the rules and the rules
/// are what a test has to be able to reach: a threshold window that is not clamped never surfaces
/// the renew button, a negative rate would sell a renewal for nothing, and a discount bigger than
/// the litre price would sell fuel below cost.
/// </remarks>
public sealed class UpdateAdminSettingsCommandHandler
{
    private readonly RuntimeSettingsService _settings;

    public UpdateAdminSettingsCommandHandler(RuntimeSettingsService settings) => _settings = settings;

    public async Task<UpdateAdminSettingsResult> HandleAsync(
        UpdateAdminSettingsCommand command,
        CancellationToken cancellationToken = default)
    {
        var request = command.Request;
        var actorUserId = command.ActorUserId;
        var actorUserName = command.ActorUserName;

        if (request?.AutoRefund is null && request?.DeletedUnpaidOrderCleanup is null
            && request?.DataRetention is null && request?.ExpiredVoucherLoss is null
            && request?.VoucherRenewal is null
            && request?.VoucherTerm is null
            && request?.FeedbackBot is null)
        {
            return UpdateAdminSettingsResult.NoSettingsSupplied();
        }

        AutoRefundSettingsUpdate? autoRefundResult = null;
        DeletedUnpaidOrderCleanupSettingsUpdate? deletedUnpaidOrderCleanupResult = null;
        DataRetentionSettingsUpdate? dataRetentionResult = null;
        ExpiredVoucherLossSettingsUpdate? expiredVoucherLossResult = null;
        VoucherRenewalSettingsUpdate? voucherRenewalResult = null;
        VoucherTermSettingsUpdate? voucherTermResult = null;
        FeedbackBotSettingsUpdate? feedbackBotResult = null;

        if (request.FeedbackBot is not null)
        {
            // The toggle is honoured even while Telegram is unprovisioned, so a manager can stage
            // the decision before the env change. The app will simply keep hiding the tab until
            // both halves are true - the deep-link endpoint checks IsConfigured as well.
            await _settings.UpsertAsync(
                AppSettingKeys.FeedbackBotEnabled,
                request.FeedbackBot.Enabled.ToString(),
                actorUserId,
                actorUserName,
                cancellationToken);

            feedbackBotResult = new FeedbackBotSettingsUpdate(request.FeedbackBot.Enabled);
        }

        if (request.AutoRefund is not null)
        {
            await _settings.UpsertAsync(
                AppSettingKeys.AutoRefundEnabled,
                request.AutoRefund.Enabled.ToString(),
                actorUserId,
                actorUserName,
                cancellationToken);

            var delayDays = Math.Max(1, request.AutoRefund.DelayDays);
            await _settings.UpsertAsync(
                AppSettingKeys.AutoRefundDelayDays,
                delayDays.ToString(),
                actorUserId,
                actorUserName,
                cancellationToken);

            autoRefundResult = new AutoRefundSettingsUpdate(request.AutoRefund.Enabled, delayDays);
        }

        if (request.DeletedUnpaidOrderCleanup is not null)
        {
            await _settings.UpsertAsync(
                AppSettingKeys.DeletedUnpaidOrderCleanupEnabled,
                request.DeletedUnpaidOrderCleanup.Enabled.ToString(),
                actorUserId,
                actorUserName,
                cancellationToken);

            // Clamp to a whole day minimum so a stray 0/negative can never make every abandoned
            // order instantly purgeable; mirrors the job's own Math.Max(1, ...) guard.
            var retentionDays = Math.Max(1, request.DeletedUnpaidOrderCleanup.RetentionDays);
            await _settings.UpsertAsync(
                AppSettingKeys.DeletedUnpaidOrderCleanupRetentionDays,
                retentionDays.ToString(),
                actorUserId,
                actorUserName,
                cancellationToken);

            deletedUnpaidOrderCleanupResult = new DeletedUnpaidOrderCleanupSettingsUpdate(
                request.DeletedUnpaidOrderCleanup.Enabled, retentionDays);
        }

        if (request.DataRetention is not null)
        {
            await _settings.UpsertAsync(
                AppSettingKeys.DataRetentionEnabled,
                request.DataRetention.Enabled.ToString(),
                actorUserId,
                actorUserName,
                cancellationToken);

            dataRetentionResult = new DataRetentionSettingsUpdate(request.DataRetention.Enabled);
        }

        if (request.ExpiredVoucherLoss is not null)
        {
            await _settings.UpsertAsync(
                AppSettingKeys.ExpiredVoucherLossEnabled,
                request.ExpiredVoucherLoss.Enabled.ToString(),
                actorUserId,
                actorUserName,
                cancellationToken);

            expiredVoucherLossResult = new ExpiredVoucherLossSettingsUpdate(request.ExpiredVoucherLoss.Enabled);
        }

        if (request.VoucherRenewal is not null)
        {
            await _settings.UpsertAsync(
                AppSettingKeys.VoucherRenewalEnabled,
                request.VoucherRenewal.Enabled.ToString(),
                actorUserId,
                actorUserName,
                cancellationToken);

            // Clamp the trigger window to a sane whole-day range: at least 1 day (a 0/negative window
            // would never surface the button) and at most a year (a runaway value would offer renewal
            // on vouchers that are nowhere near expiry).
            var thresholdDays = Math.Clamp(request.VoucherRenewal.TriggerThresholdDays, 1, 365);
            await _settings.UpsertAsync(
                AppSettingKeys.VoucherRenewalTriggerThresholdDays,
                thresholdDays.ToString(),
                actorUserId,
                actorUserName,
                cancellationToken);

            var tierResults = new List<VoucherRenewalTierUpdate>();
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
                    actorUserId,
                    actorUserName,
                    cancellationToken);

                // A price can never be negative; a zero rate is allowed but leaves the tier not-offerable
                // (IsOfferable requires rate > 0), so an admin can pre-enable a tier before pricing it.
                var rate = Math.Max(0m, tier.RatePerLiterUah);
                await UpsertTierAsync(
                    VoucherTermPricingSchemes.RenewalRate,
                    code,
                    tier.Enabled,
                    rate,
                    actorUserId,
                    actorUserName,
                    cancellationToken);

                await _settings.UpsertAsync(
                    AppSettingKeys.VoucherRenewalTierRatePerLiter(code),
                    rate.ToString(CultureInfo.InvariantCulture),
                    actorUserId,
                    actorUserName,
                    cancellationToken);

                tierResults.Add(new VoucherRenewalTierUpdate(code, tier.Enabled, rate));
            }

            voucherRenewalResult = new VoucherRenewalSettingsUpdate(
                request.VoucherRenewal.Enabled,
                thresholdDays,
                tierResults);
        }

        if (request.VoucherTerm is not null)
        {
            await _settings.UpsertAsync(
                AppSettingKeys.VoucherTermSaleEnabled,
                request.VoucherTerm.Enabled.ToString(),
                actorUserId,
                actorUserName,
                cancellationToken);

            var termTierResults = new List<VoucherTermTierUpdate>();
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
                    actorUserId,
                    actorUserName,
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
                    actorUserId,
                    actorUserName,
                    cancellationToken);

                await _settings.UpsertAsync(
                    AppSettingKeys.VoucherTermTierDiscountPerLiter(code),
                    discount.ToString(CultureInfo.InvariantCulture),
                    actorUserId,
                    actorUserName,
                    cancellationToken);

                termTierResults.Add(new VoucherTermTierUpdate(code, tier.Enabled, discount));
            }

            voucherTermResult = new VoucherTermSettingsUpdate(
                request.VoucherTerm.Enabled,
                termTierResults);
        }

        return new UpdateAdminSettingsResult(
            true,
            null,
            autoRefundResult,
            deletedUnpaidOrderCleanupResult,
            dataRetentionResult,
            expiredVoucherLossResult,
            voucherRenewalResult,
            voucherTermResult,
            feedbackBotResult);
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
        Guid? actorUserId,
        string? actorUserName,
        CancellationToken cancellationToken)
        => _settings.SetTierAsync(
            scheme,
            termCode,
            enabled,
            ratePerLiter,
            actorUserId,
            actorUserName,
            cancellationToken);
}

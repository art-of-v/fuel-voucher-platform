using FuelFlow.Features.Feedback;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Vouchers.Renewal;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FuelFlow.Features.Settings.GetAdminSettings;

/// <summary>
/// Assembles the whole admin settings payload: the runtime switches, the two voucher ladders and
/// the margin every fuel can actually carry.
/// </summary>
/// <remarks>
/// The reading lives here rather than in the controller because the settings screen is the only
/// place that sees all of it at once: the switches come from <c>app_settings</c> through the
/// runtime settings service, the ladders from the same place, and the per-fuel margins from the
/// fuel catalogue. The controller only turns the answer into a response.
/// </remarks>
public sealed class GetAdminSettingsQueryHandler
{
    private readonly RuntimeSettingsService _settings;
    private readonly ApplicationDbContext _context;
    private readonly ITelegramFeedbackClient _feedbackBot;
    private readonly TelegramFeedbackOptions _feedbackOptions;

    public GetAdminSettingsQueryHandler(
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

    public async Task<SettingsDto> HandleAsync(
        GetAdminSettingsQuery query,
        CancellationToken cancellationToken = default)
    {
        var enabled = await _settings.IsAutoRefundEnabledAsync(cancellationToken);
        var delayDays = await _settings.GetAutoRefundDelayDaysAsync(cancellationToken);

        var cleanupEnabled = await _settings.IsDeletedUnpaidOrderCleanupEnabledAsync(cancellationToken);
        var cleanupRetentionDays = await _settings.GetDeletedUnpaidOrderCleanupRetentionDaysAsync(cancellationToken);

        var dataRetentionEnabled = await _settings.IsDataRetentionEnabledAsync(cancellationToken);

        var expiredVoucherLossEnabled = await _settings.IsExpiredVoucherLossEnabledAsync(cancellationToken);

        var renewal = await _settings.GetVoucherRenewalConfigAsync(cancellationToken);
        var termSale = await _settings.GetVoucherTermConfigAsync(cancellationToken);

        return new SettingsDto
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
                Webhook = await FeedbackWebhookStatusReader.ReadAsync(
                    _feedbackBot, _feedbackOptions, cancellationToken)
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
                MarginFloorUah = AdminSettingsPolicy.TermMarginFloorUah,
                Fuels = (await TermMarginsAsync(cancellationToken))
                    .OrderBy(f => f.MarginPerLiterUah)
                    .ToList()
            }
        };
    }

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
        // Materialised before the projection, because the customer price is not a column any more:
        // it is `cost + profit` over two nullable columns, and the decision that a missing knob
        // means "no price of ours" belongs to the one function that owns it. The row count is one
        // per fuel - 25 on production - so this is a list, not a scan.
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
}

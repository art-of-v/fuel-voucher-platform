using System.Text.Json;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Pricing.ImportOkkoPumpPrices;
using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Pricing.BackfillBrandFuelCodes;

public sealed record BackfillBrandFuelCodesCommand(IReadOnlyList<OkkoPumpPrice> Sheet, bool DryRun);

/// <summary>One fuel the backfill would code, with the published code it came from.</summary>
public sealed record BrandFuelCodeFill(string FuelTypeId, string Name, string Category, string Code);

/// <summary>A fuel the backfill refused to touch, and the reason it will not.</summary>
public sealed record BrandFuelCodeSkipped(string Name, string Reason, string? Candidates);

public sealed record BackfillBrandFuelCodesResult(
    int Filled,
    int AlreadyCoded,
    IReadOnlyList<BrandFuelCodeFill> Fills,
    IReadOnlyList<BrandFuelCodeSkipped> Skipped,
    bool DryRun);

/// <summary>
/// Gives the fuels an operator typed the brand's own code, from the brand's own published list.
/// Planning #349, step 6b.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists at all.</b> Twenty-eight of our fuels were added one at a time through the brand
/// screen, and the id those writes generate says nothing — so they carry no code, which means the price
/// import still resolves them by <em>category</em>, and two fuels of one category are then
/// indistinguishable. Five seeded OKKO fuels were coded by hand in #1199 because their codes could be
/// read off the published list; this is the same reading, applied to the rows that have no such
/// shortcut, because the only other way to learn a code is to ask the brand.
/// </para>
/// <para>
/// <b>It is a derivation, so it is held to a high bar.</b> A fuel's name gives a canonical category;
/// the sheet gives the codes that mean that category; a code is proposed only when exactly one of them
/// does. Everything else is reported and left alone: no code on the sheet, several codes for the grade,
/// or another live fuel already holding the candidate. A wrong code here is not a cosmetic mistake —
/// it is an identity <c>ux_fuel_types_Brand_brand_fuel_code</c> then enforces, and the price import
/// would hand that fuel somebody else's pump.
/// </para>
/// <para>
/// <b>It never touches a fuel that already has a code,</b> never archives, and never prices anything.
/// It runs after the catalogue import in the same job, so a fuel created a minute ago already carries
/// its own code and is skipped — which is what makes it safe to leave in a fifteen-minute schedule: once
/// it has done its work it does nothing, and a fuel created afterwards is filled the same way.
/// </para>
/// </remarks>
public sealed class BackfillBrandFuelCodesCommandHandler
{
    /// <summary>
    /// The network this backfill reads, as the name a supplier publishes it under. Resolved to an id
    /// per run - brands are keyed by uuid (planning D-21) - and the slug itself still names the audit
    /// scope, because that is what the scope column has always held.
    /// </summary>
    private const string OkkoSlug = "okko";

    private readonly ApplicationDbContext _context;
    private readonly BrandSlugResolver _brands;
    private readonly AuditEventService _events;

    public BackfillBrandFuelCodesCommandHandler(
        ApplicationDbContext context,
        BrandSlugResolver brands,
        AuditEventService events)
    {
        _context = context;
        _brands = brands;
        _events = events;
    }

    public async Task<BackfillBrandFuelCodesResult> ApplyAsync(
        BackfillBrandFuelCodesCommand command,
        Guid actingUserId,
        string? actingUserName,
        CancellationToken ct = default)
    {
        // Nothing to backfill into a network that is not in the catalogue. Silent success would be
        // worse than a skip: the job would report "0 filled" every night and read as done.
        var okkoId = await _brands.ResolveAsync(OkkoSlug, ct);
        if (okkoId is null)
            throw new InvalidOperationException(
                $"No live brand carries the slug '{OkkoSlug}', so there is no OKKO catalogue to backfill.");

        // The codes the brand publishes, grouped by the category each one means. Two codes in one
        // category is not an error here — diesel genuinely has several papers — it is simply a code we
        // cannot choose between, and it is reported as such per fuel.
        var codesByCategory = new Dictionary<OkkoFuelCategory, List<string>>();
        foreach (var row in command.Sheet)
        {
            var code = FuelTypeEntity.NormaliseBrandFuelCode(row.SiteFuelCode);
            if (code is null) continue;
            if (OkkoFuelClassifier.CategoryFromSiteFuelCode(code) is not { } category) continue;

            if (!codesByCategory.TryGetValue(category, out var list))
                codesByCategory[category] = list = new List<string>();
            if (!list.Contains(code, StringComparer.Ordinal)) list.Add(code);
        }

        // Live rows only: an archived fuel is history, and coding it would only add a second answer to
        // "which fuel does this code mean".
        //
        // Ordered in memory, like the price importer does: the ordinal comparer that makes "which of two
        // fuels of one grade is first" deterministic is not translatable by EF, and the row count here is
        // a handful either way.
        var uncoded = (await _context.FuelTypes
                .AsTracking()
                .Where(f => f.BrandId == okkoId.Value && f.BrandFuelCode == null && !f.IsDeleted)
                .ToListAsync(ct))
            .OrderBy(f => f.Id, StringComparer.Ordinal)
            .ToList();

        var taken = (await _context.FuelTypes
                .AsNoTracking()
                .Where(f => f.BrandId == okkoId.Value && f.BrandFuelCode != null && !f.IsDeleted)
                .Select(f => f.BrandFuelCode!)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var fills = new List<BrandFuelCodeFill>();
        var skipped = new List<BrandFuelCodeSkipped>();
        var now = DateTime.UtcNow;

        foreach (var fuel in uncoded)
        {
            var category = OkkoFuelClassifier.CategoryFromName(fuel.Name);

            if (category is null)
            {
                skipped.Add(new BrandFuelCodeSkipped(fuel.Name, "its name is not a grade this catalogue sells", null));
                continue;
            }

            if (!codesByCategory.TryGetValue(category.Value, out var candidates) || candidates.Count == 0)
            {
                skipped.Add(new BrandFuelCodeSkipped(fuel.Name, "the brand publishes no code for this grade", null));
                continue;
            }

            if (candidates.Count > 1)
            {
                skipped.Add(new BrandFuelCodeSkipped(
                    fuel.Name,
                    $"the brand publishes {candidates.Count} codes for this grade and this cannot choose between them",
                    string.Join(", ", candidates)));
                continue;
            }

            var code = candidates[0];
            if (taken.Contains(code))
            {
                skipped.Add(new BrandFuelCodeSkipped(
                    fuel.Name, $"another live fuel already holds '{code}'", code));
                continue;
            }

            taken.Add(code);
            fills.Add(new BrandFuelCodeFill(fuel.Id, fuel.Name, category.Value.ToString(), code));

            if (!command.DryRun)
            {
                fuel.BrandFuelCode = code;
                fuel.UpdatedAtUtc = now;
            }
        }

        if (!command.DryRun && fills.Count > 0)
        {
            await _context.SaveChangesAsync(ct);

            foreach (var fill in fills)
            {
                await _events.RecordEventAsync(
                    "Fuel", fill.FuelTypeId, "BrandFuelCodeBackfilled",
                    null,
                    JsonSerializer.Serialize(new { fill.Name, Category = fill.Category, BrandFuelCode = fill.Code }),
                    actingUserId == Guid.Empty ? Guid.Empty : actingUserId,
                    actingUserName,
                    $"{fill.Name}: matched OKKO's published code {fill.Code}",
                    OkkoSlug,
                    ct);
            }
        }

        return new BackfillBrandFuelCodesResult(
            Filled: fills.Count,
            AlreadyCoded: taken.Count,
            Fills: fills,
            Skipped: skipped,
            DryRun: command.DryRun);
    }
}

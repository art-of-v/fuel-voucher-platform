using System.Globalization;
using System.Text.RegularExpressions;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Vouchers;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace FuelFlow.Features.Vouchers.Import;

public sealed class KloVoucherParser : IVoucherProviderParser
{
    /// <summary>See <see cref="OkkoVoucherParser"/> for why this is a slug and not an id.</summary>
    private const string BrandSlug = "klo";

    private readonly ApplicationDbContext _context;
    private readonly BrandSlugResolver _brands;
    private readonly ILogger<KloVoucherParser> _logger;

    public KloVoucherParser(
        ApplicationDbContext context,
        BrandSlugResolver brands,
        ILogger<KloVoucherParser> logger)
    {
        _context = context;
        _brands = brands;
        _logger = logger;
    }

    private static readonly Regex LitersRegex = new(@"(\d+(?:[.,]\d+)?)\s*(?:л|l)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DateRegex = new(@"\b(\d{2})[./-](\d{2})[./-](\d{4})\b", RegexOptions.Compiled);
    private static readonly Regex VoucherNumberRegex = new(@"\b\d{16,20}\b", RegexOptions.Compiled);
    private static readonly Regex FuelTypeRegex = new(
        @"\b(?<fuel>[АA]\s*[-–—]?\s*\d{2,3}(?:\s*\+)?|Д\s*[ПP]|ГАЗ)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BrandNameRegex = new(@"(?:КПП\s*К[5-6]\.?|КПП\s*К[\d\.]+\s*\d{3,4}|КПП\s*[\d\.]+\s*\d{3,4}|КПП\s*\d+\.?\s*\d{1,2})[\s\S]*?(?=\d{2}[./-](\d{2})[./-]\d{4})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public bool CanParse(ProviderDetectionContext context)
    {
        return context.Words.Any(w =>
            w.Text.Contains("КЛО", StringComparison.OrdinalIgnoreCase) ||
            w.Text.Contains("KLO", StringComparison.OrdinalIgnoreCase) ||
            w.Text.Contains("klo.ua", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyCollection<ParsedVoucher>> ParseAsync(
        ProviderParseContext context,
        CancellationToken cancellationToken)
    {
        var parsedVouchers = new List<ParsedVoucher>();

        var kloId = await _brands.ResolveAsync(BrandSlug, cancellationToken);
        if (kloId is null)
        {
            _logger.LogWarning(
                "Skipping the KLO page: no live brand carries the slug {Slug}.", BrandSlug);
            return parsedVouchers;
        }

        var kloFuelTypes = await _context.FuelTypes
            .Where(f => f.BrandId == kloId.Value)
            .ToListAsync(cancellationToken);

        foreach (var region in context.VoucherRegions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var voucherWords = context.PageRender.Words
                .Where(w => region.Contains(w.BoundingBox))
                .ToList();

            var lines = voucherWords
                .GroupBy(w => Math.Round(w.BoundingBox.Bottom, 1))
                .OrderByDescending(g => g.Key)
                .Select(g => string.Join(" ", g.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text)));

            var rawText = string.Join("\n", lines);

            var liters = ParseLiters(rawText);
            var expirationDate = ParseExpirationDate(rawText);
            var voucherNumber = ParseVoucherNumber(rawText);
            var fuelTypeName = ParseFuelTypeName(rawText);
            var BrandName = ParseBrandName(rawText);

            string? qrPayload = null;
            QrDecodeResult qrResult = new();
            try
            {
                using var croppedImage = context.PageRender.Image.Clone(x => x.Crop(region.Bounds));
                qrResult = context.QrDecoder.Decode(croppedImage);
                qrPayload = qrResult.Text;
            }
            catch (Exception ex)
            {
                // Best-effort QR decode; log the previously-swallowed failure so a systemic
                // breakage is visible instead of silently degrading import quality.
                _logger.LogWarning(ex,
                    "KLO QR decode failed for region {Region} on page {PageNumber}.",
                    region.Bounds, context.PageRender.PageNumber);
            }

            // The fuel is found by the GRADE, not by the string: the catalogue says a grade once,
            // so "ГАЗ" and "Газ" are one fuel and the row the catalogue holds is the row a voucher
            // must land on. The grade key is the name folded onto one spelling - the same rule the
            // database's ux_fuel_types_brand_grade_key enforces.
            //
            // A grade the catalogue does not hold stays unresolved: the parsed name says which grade
            // it is, and a guess at a name the catalogue does not carry would land the voucher on
            // whatever row sorted first.
            var gradeKey = FuelGrade.Key(fuelTypeName);
            var fuelTypeEntity = gradeKey is null
                ? null
                : kloFuelTypes.FirstOrDefault(f => FuelGrade.Key(f.Name) == gradeKey);

            // No invented id when the name does not match, which is what this used to do: the fallback was the
            // literal "klo-dp", a natural key that exists only in the rows
            // `20260618224553_AddStationCatalog` seeded. Production has no such row - all 28 of its
            // fuels carry a generated uuid - and `fuel_vouchers.fuel_type_id` carries no foreign key
            // to catch it, so the import inserted a voucher naming a fuel that does not exist and
            // nothing downstream complained. OkkoVoucherParser and WogVoucherParser have always left
            // it null; this is the same shape, and the confidence score below already records that
            // the grade was not identified.
            var fuelTypeId = fuelTypeEntity?.Id;
            decimal confidence = fuelTypeEntity != null ? 20 : 0;
            if (liters > 0) confidence += 20;
            if (expirationDate != default) confidence += 20;
            if (!string.IsNullOrEmpty(voucherNumber)) confidence += 20;
            if (!string.IsNullOrEmpty(qrPayload)) confidence += 20;
            if (!string.IsNullOrEmpty(BrandName)) confidence += 5;

            parsedVouchers.Add(new ParsedVoucher
            {
                Brand = BrandSlug,
                FuelTypeId = fuelTypeId,
                Liters = liters,
                ExpirationDate = expirationDate,
                VoucherNumber = voucherNumber,
                QrPayload = qrPayload ?? string.Empty,
                QrEccLevel = qrResult.EccLevel,
                QrVersion = qrResult.Version,
                QrMaskPattern = qrResult.MaskPattern,
                QrEncodingMode = qrResult.EncodingMode,
                OriginalQrMatrix = qrResult.OriginalMatrix,
                Confidence = confidence,
                RawText = rawText
            });
        }

        return await Task.FromResult(parsedVouchers);
    }

    private static decimal ParseLiters(string text)
    {
        var match = LitersRegex.Match(text);
        if (match.Success)
        {
            var value = match.Groups[1].Value.Replace(',', '.');
            if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var liters))
                return liters;
        }
        return 0;
    }

    private static DateOnly ParseExpirationDate(string text)
    {
        var match = DateRegex.Match(text);
        if (match.Success)
        {
            var cleaned = $"{match.Groups[1].Value}.{match.Groups[2].Value}.{match.Groups[3].Value}";
            if (DateOnly.TryParseExact(cleaned, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return date;
        }
        return default;
    }

    private static string ParseVoucherNumber(string text)
    {
        var match = VoucherNumberRegex.Match(text);
        return match.Success ? match.Value : string.Empty;
    }

    private static string? ParseFuelTypeName(string text)
    {
        var match = FuelTypeRegex.Match(text);
        if (!match.Success) return null;

        var raw = match.Groups["fuel"].Value
            .Replace(" ", "")
            .Replace("-", "")
            .Replace("–", "")
            .Replace("—", "")
            .Replace('\u0410', 'A');

        var hasMustang = text.Contains("Mustang", StringComparison.OrdinalIgnoreCase);

        return raw.ToUpperInvariant() switch
        {
            "A95" or "A95+" or "95" => hasMustang ? "A-95 Mustang" : "A 95 EURO",
            "A98" or "98" => "Mustang 100",
            "ДП" or "Д" => "ДП Mustang",
            "ГАЗ" => "ГАЗ",
            _ => null
        };
    }

    private static string ParseBrandName(string text)
    {
        var match = BrandNameRegex.Match(text);
        return match.Success ? match.Value.Trim() : string.Empty;
    }
}

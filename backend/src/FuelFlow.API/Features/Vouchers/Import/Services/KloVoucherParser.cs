using System.Globalization;
using System.Text.RegularExpressions;
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
    private readonly ApplicationDbContext _context;
    private readonly ILogger<KloVoucherParser> _logger;

    public KloVoucherParser(ApplicationDbContext context, ILogger<KloVoucherParser> logger)
    {
        _context = context;
        _logger = logger;
    }

    private static readonly Regex LitersRegex = new(@"(\d+(?:[.,]\d+)?)\s*(?:л|l)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DateRegex = new(@"\b(\d{2})[./-](\d{2})[./-](\d{4})\b", RegexOptions.Compiled);
    private static readonly Regex VoucherNumberRegex = new(@"\b\d{16,20}\b", RegexOptions.Compiled);
    private static readonly Regex FuelTypeRegex = new(
        @"\b(?<fuel>[АA]\s*[-–—]?\s*\d{2,3}(?:\s*\+)?|Д\s*[ПP]|ГАЗ)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex StationNameRegex = new(@"(?:КПП\s*К[5-6]\.?|КПП\s*К[\d\.]+\s*\d{3,4}|КПП\s*[\d\.]+\s*\d{3,4}|КПП\s*\d+\.?\s*\d{1,2})[\s\S]*?(?=\d{2}[./-](\d{2})[./-]\d{4})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

        var kloFuelTypes = await _context.FuelTypes
            .Where(f => f.StationId == "klo")
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
            var stationName = ParseStationName(rawText);

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

            var fuelTypeEntity = await _context.FuelTypes
                .FirstOrDefaultAsync(f => f.StationId == "klo" && f.Name == fuelTypeName, cancellationToken);

            var fuelTypeId = fuelTypeEntity?.Id ?? "klo-dp";
            decimal confidence = fuelTypeEntity != null ? 20 : 0;
            if (liters > 0) confidence += 20;
            if (expirationDate != default) confidence += 20;
            if (!string.IsNullOrEmpty(voucherNumber)) confidence += 20;
            if (!string.IsNullOrEmpty(qrPayload)) confidence += 20;
            if (!string.IsNullOrEmpty(stationName)) confidence += 5;

            parsedVouchers.Add(new ParsedVoucher
            {
                Provider = "KLO",
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

    private static string ParseFuelTypeName(string text)
    {
        var match = FuelTypeRegex.Match(text);
        if (!match.Success) return "ДП Mustang";

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
            "ДП" or "Д" => hasMustang ? "ДП Mustang" : "ДП Mustang",
            "ГАЗ" => "ГАЗ",
            _ => "ДП Mustang"
        };
    }

    private static string ParseStationName(string text)
    {
        var match = StationNameRegex.Match(text);
        return match.Success ? match.Value.Trim() : string.Empty;
    }
}

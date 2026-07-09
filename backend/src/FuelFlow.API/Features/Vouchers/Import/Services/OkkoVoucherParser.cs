using System.Globalization;
using System.Text.RegularExpressions;
using FuelFlow.Features.Vouchers;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace FuelFlow.Features.Vouchers.Import;

public sealed class OkkoVoucherParser : IVoucherProviderParser
{
    private readonly ApplicationDbContext _context;

    private static readonly Regex LitersRegex = new(@"(\d+(?:[.,]\d+)?)\s*(?:л|l)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DateRegex = new(@"\b(\d{2})[./-](\d{2})[./-](\d{4})\b", RegexOptions.Compiled);
    private static readonly Regex VoucherNumberRegex = new(@"\b\d{16,20}\b", RegexOptions.Compiled);

    public OkkoVoucherParser(ApplicationDbContext context)
    {
        _context = context;
    }

    public bool CanParse(ProviderDetectionContext context)
    {
        return context.Words.Any(w =>
            w.Text.Contains("OKKO", StringComparison.OrdinalIgnoreCase) ||
            w.Text.Contains("ОККО", StringComparison.OrdinalIgnoreCase) ||
            w.Text.Contains("okko.ua", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyCollection<ParsedVoucher>> ParseAsync(
        ProviderParseContext context,
        CancellationToken cancellationToken)
    {
        var parsedVouchers = new List<ParsedVoucher>();

        var okkoFuelTypes = await _context.FuelTypes
            .Where(f => f.StationId == "okko")
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

            QrDecodeResult qrResult = new();
            try
            {
                using var croppedImage = context.PageRender.Image.Clone(x => x.Crop(region.Bounds));
                qrResult = context.QrDecoder.Decode(croppedImage);
            }
            catch (Exception)
            {
            }

            var qrPayload = qrResult.Text;

            var fuelTypeEntity = ResolveFuelType(rawText, qrPayload, okkoFuelTypes);
            var fuelTypeId = fuelTypeEntity?.Id;
            decimal confidence = fuelTypeEntity != null ? 20 : 0;
            if (liters > 0) confidence += 20;
            if (expirationDate != default) confidence += 20;
            if (!string.IsNullOrEmpty(voucherNumber)) confidence += 20;
            if (!string.IsNullOrEmpty(qrPayload)) confidence += 20;

            parsedVouchers.Add(new ParsedVoucher
            {
                Provider = "OKKO",
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

    private static FuelTypeEntity? ResolveFuelType(
        string rawText,
        string? qrPayload,
        List<FuelTypeEntity> okkoFuelTypes)
    {
        foreach (var ft in okkoFuelTypes.OrderByDescending(f => f.Name.Length))
        {
            if (rawText.Contains(ft.Name, StringComparison.OrdinalIgnoreCase))
                return ft;
        }

        if (!string.IsNullOrEmpty(qrPayload))
        {
            var name = ParseFuelTypeNameFromQr(qrPayload);
            return okkoFuelTypes.FirstOrDefault(f => f.Name == name);
        }

        return null;
    }

    private static string ParseFuelTypeNameFromQr(string qrPayload)
    {
        var match = Regex.Match(qrPayload, @"^(\d+)\$");
        if (match.Success)
        {
            return match.Groups[1].Value switch
            {
                "9018" or "9518" => "ДП ЄВРО",
                "45290" => "ДП PULLS",
                "9015" or "9016" or "9515" or "9009" => "A-95",
                "9019" or "9020" => "ГАЗ",
                _ => "A-95"
            };
        }
        return "A-95";
    }
}

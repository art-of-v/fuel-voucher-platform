using System.Globalization;
using System.Text.RegularExpressions;
using FuelFlow.Features.Vouchers;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace FuelFlow.Features.Vouchers.Import;

public sealed class WogVoucherParser : IVoucherProviderParser
{
    private readonly ApplicationDbContext _context;

    public WogVoucherParser(ApplicationDbContext context)
    {
        _context = context;
    }

    private static readonly Regex LitersRegex = new(@"(\d+(?:[.,]\d+)?)\s*(?:л|l)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DateRegex = new(@"\b(\d{2})[./-](\d{2})[./-](\d{4})\b", RegexOptions.Compiled);
    private static readonly Regex VoucherNumberRegex = new(@"\b\d{16,20}\b", RegexOptions.Compiled);
    private static readonly Regex FuelTypeRegex = new(
        @"\b(?<fuel>[АA]\s*[-–—]?\s*\d{2,3}(?:\s*\+)?|Д\s*[ПP]|ГАЗ)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public bool CanParse(ProviderDetectionContext context)
    {
        return context.Words.Any(w =>
            w.Text.Contains("WOG", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyCollection<ParsedVoucher>> ParseAsync(
        ProviderParseContext context,
        CancellationToken cancellationToken)
    {
        var parsedVouchers = new List<ParsedVoucher>();
        var regions = context.VoucherRegions.ToList();

        foreach (var region in regions)
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

            string? qrPayload = null;
            QrDecodeResult qrResult = new();
            try
            {
                using var croppedImage = context.PageRender.Image.Clone(x => x.Crop(region.Bounds));
                qrResult = context.QrDecoder.Decode(croppedImage);
                qrPayload = qrResult.Text;
            }
            catch (Exception)
            {
            }

            // Retry with an expanded crop when:
            //   • the payload was not decoded at all, OR
            //   • the payload decoded but the mask is null — ZXing's lenient BarcodeReader
            //     can read a payload from a small crop while the stricter Detector (needed
            //     for mask extraction) requires a larger, better-aligned image region.
            // ExpandBounds never grows downward, so the next row's QR cannot bleed in.
            if (string.IsNullOrEmpty(qrPayload) || qrResult.MaskPattern == null)
            {
                try
                {
                    var expanded = ExpandBounds(region.Bounds, context.PageRender.Image);
                    using var expandedCrop = context.PageRender.Image.Clone(x => x.Crop(expanded));
                    var retryResult = context.QrDecoder.Decode(expandedCrop);

                    if (!string.IsNullOrEmpty(retryResult.Text))
                    {
                        // Retry produced a full result — use it entirely.
                        qrResult = retryResult;
                        qrPayload = retryResult.Text;
                    }
                    else if (retryResult.MaskPattern != null && !string.IsNullOrEmpty(qrPayload))
                    {
                        // Retry got metadata but no payload — merge: keep first-pass payload,
                        // take mask/version/ECC/matrix from the retry (Detector had enough image).
                        qrResult = new QrDecodeResult
                        {
                            Text = qrPayload,
                            EccLevel = retryResult.EccLevel ?? qrResult.EccLevel,
                            Version = retryResult.Version ?? qrResult.Version,
                            MaskPattern = retryResult.MaskPattern,
                            EncodingMode = retryResult.EncodingMode ?? qrResult.EncodingMode,
                            OriginalMatrix = retryResult.OriginalMatrix
                        };
                    }
                }
                catch (Exception)
                {
                }
            }

            var fuelTypeEntity = await _context.FuelTypes
                .FirstOrDefaultAsync(f => f.StationId == "wog" && f.Name == fuelTypeName, cancellationToken);

            var fuelTypeId = fuelTypeEntity?.Id ?? "wog-dp";
            decimal confidence = fuelTypeEntity != null ? 20 : 0;
            if (liters > 0) confidence += 20;
            if (expirationDate != default) confidence += 20;
            if (!string.IsNullOrEmpty(voucherNumber)) confidence += 20;
            if (!string.IsNullOrEmpty(qrPayload)) confidence += 20;

            parsedVouchers.Add(new ParsedVoucher
            {
                Provider = "WOG",
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

    private static Rectangle ExpandBounds(Rectangle bounds, Image pageImage)
    {
        // The WOG QR code is always in the top-right corner of the voucher cell.
        // Neighbours below and on the sides must not bleed in, so we expand upward only.
        // Padding = half the cell height gives the Detector enough quiet-zone headroom.
        var padding = bounds.Height / 2;
        var x = bounds.X;                                  // left edge stays fixed
        var y = Math.Max(0, bounds.Y - padding);           // expand upward only
        var w = Math.Min(pageImage.Width - x, bounds.Width); // width unchanged
        var h = bounds.Bottom - y;                         // bottom edge stays fixed
        return new Rectangle(x, y, w, h);
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
}

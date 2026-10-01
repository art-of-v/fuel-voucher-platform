using System.Globalization;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.ParseInvoice;

/// <summary>
/// Matches the fuel lines read off a supplier invoice against the batches of one voucher import and
/// builds a <see cref="ParseInvoiceResultDto"/> preview: a suggested cost/liter per fuel for the
/// operator to review and commit via the manual cost PUT. Pure read — no writes, no repricing.
/// </summary>
public sealed record ParseInvoiceCommand(
    Guid ImportId,
    IReadOnlyList<InvoiceLine> Lines,
    IReadOnlyList<ParseInvoiceIssue> ParseErrors);

public sealed record ParseInvoiceResult(bool NotFound, ParseInvoiceResultDto? Dto);

public sealed class ParseInvoiceCommandHandler
{
    private readonly ApplicationDbContext _context;

    public ParseInvoiceCommandHandler(ApplicationDbContext context) => _context = context;

    public async Task<ParseInvoiceResult> HandleAsync(ParseInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        var importExists = await _context.VoucherImports.AnyAsync(i => i.Id == command.ImportId, cancellationToken);
        if (!importExists)
            return new ParseInvoiceResult(true, null);

        // Roll the import's vouchers up per fuel — same shape as the batch-costs tab.
        var groups = await _context.FuelVouchers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(v => v.ImportJobId == command.ImportId)
            .GroupBy(v => new { v.FuelTypeId, v.Provider })
            .Select(g => new { g.Key.FuelTypeId, g.Key.Provider, TotalLiters = g.Sum(v => v.Liters) })
            .ToListAsync(cancellationToken);

        var fuelIds = groups.Select(g => g.FuelTypeId).Distinct().ToList();
        var names = await _context.FuelTypes
            .AsNoTracking()
            .Where(f => fuelIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.Name, cancellationToken);

        var batches = groups
            .Select(g => new ImportFuel(
                g.FuelTypeId,
                names.TryGetValue(g.FuelTypeId, out var n) ? n : null,
                g.Provider,
                g.TotalLiters))
            .ToList();

        var dto = new ParseInvoiceResultDto { Errors = command.ParseErrors.ToList() };
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in command.Lines)
        {
            var cost = line.CostPerLiter;
            string? warning = null;

            if ((cost is null || cost <= 0m) && line is { Total: > 0m, Liters: > 0m })
            {
                cost = decimal.Round(line.Total.Value / line.Liters.Value, 4, MidpointRounding.AwayFromZero);
                warning = Append(warning, "cost derived from total ÷ quantity");
            }

            var lineDto = new ParsedInvoiceLineDto
            {
                Line = line.Line,
                InvoiceLabel = line.Label,
                CostPerLiter = cost is > 0m ? cost : null,
                Liters = line.Liters,
                Total = line.Total,
            };

            var fuel = Match(line, batches);
            if (fuel is not null)
            {
                lineDto.Matched = true;
                lineDto.FuelTypeId = fuel.FuelTypeId;
                lineDto.FuelTypeName = fuel.FuelTypeName;
                lineDto.Provider = fuel.Provider;
                lineDto.ImportLiters = fuel.TotalLiters;
                matched.Add(fuel.FuelTypeId);

                if (lineDto.CostPerLiter is null)
                    warning = Append(warning, "could not determine a positive cost/liter");

                if (line.Liters is > 0m && fuel.TotalLiters > 0m)
                {
                    var diff = Math.Abs(line.Liters.Value - fuel.TotalLiters);
                    if (diff > fuel.TotalLiters * 0.01m)
                        warning = Append(warning,
                            $"invoice quantity {Fmt(line.Liters.Value)} л ≠ import {Fmt(fuel.TotalLiters)} л");
                }
            }
            else
            {
                warning = Append(warning, "no fuel in this import matches this line");
            }

            lineDto.Warning = warning;
            dto.Lines.Add(lineDto);
        }

        dto.UnmatchedImportFuels = batches
            .Where(b => !matched.Contains(b.FuelTypeId))
            .Select(b => b.FuelTypeName ?? b.FuelTypeId)
            .OrderBy(x => x)
            .ToList();

        return new ParseInvoiceResult(false, dto);
    }

    /// <summary>Resolve an invoice line to a batch: exact code first, then a normalised name/id overlap.</summary>
    private static ImportFuel? Match(InvoiceLine line, List<ImportFuel> batches)
    {
        if (!string.IsNullOrWhiteSpace(line.Code))
        {
            var byCode = batches.FirstOrDefault(b => string.Equals(b.FuelTypeId, line.Code, StringComparison.OrdinalIgnoreCase));
            if (byCode is not null) return byCode;
        }

        var label = Normalize(line.Label);
        if (label.Length == 0) return null;

        ImportFuel? byId = null;
        foreach (var b in batches)
        {
            var name = Normalize(b.FuelTypeName ?? string.Empty);
            if (name.Length > 0 && (name.Contains(label) || label.Contains(name)))
                return b;

            var id = Normalize(b.FuelTypeId);
            if (id.Length > 0 && (label.Contains(id) || id.Contains(label)))
                byId ??= b;
        }
        return byId;
    }

    private static string Normalize(string s) =>
        new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static string Fmt(decimal v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Append(string? existing, string add) =>
        string.IsNullOrEmpty(existing) ? add : $"{existing}; {add}";

    private sealed record ImportFuel(string FuelTypeId, string? FuelTypeName, string Provider, decimal TotalLiters);
}

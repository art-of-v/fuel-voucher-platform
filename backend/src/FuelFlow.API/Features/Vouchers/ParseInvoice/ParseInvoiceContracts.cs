namespace FuelFlow.Features.Vouchers.ParseInvoice;

/// <summary>
/// One fuel line item lifted from a supplier invoice (накладна), before it is matched against an
/// import. The parser fills whatever the sheet offered; cost derivation and matching happen later.
/// </summary>
public sealed class InvoiceLine
{
    /// <summary>1-based row number in the worksheet, for operator-facing error messages.</summary>
    public int Line { get; set; }

    /// <summary>The fuel name as written on the invoice ("Дизельне паливо", "Бензин А-95").</summary>
    public string Label { get; set; } = null!;

    /// <summary>Optional supplier/fuel code column — matched verbatim against the batch FuelTypeId when present.</summary>
    public string? Code { get; set; }

    /// <summary>Price per liter straight from the invoice's price column, if it had one.</summary>
    public decimal? CostPerLiter { get; set; }

    public decimal? Liters { get; set; }
    public decimal? Total { get; set; }
}

/// <summary>Raw parser output: the line items it could read plus per-row problems (one bad row is never fatal).</summary>
public sealed class InvoiceParseResult
{
    public List<InvoiceLine> Lines { get; } = [];
    public List<ParseInvoiceIssue> Errors { get; } = [];
}

/// <summary>A non-fatal parse problem anchored to a worksheet row (0 = file-level).</summary>
public sealed record ParseInvoiceIssue(int Line, string Message);

/// <summary>
/// Preview of a supplier invoice parsed against one voucher import (pricing epic RQ-3). Read-only:
/// the operator reviews the suggested cost/liter per fuel and commits each line through the existing
/// manual cost PUT, so nothing here writes to the database or reprices anything on its own.
/// </summary>
public sealed class ParseInvoiceResultDto
{
    /// <summary>One entry per invoice line item — each either matched to a fuel in this import or flagged unmatched.</summary>
    public List<ParsedInvoiceLineDto> Lines { get; set; } = [];

    /// <summary>Fuels present in the import that no invoice line matched — the operator still costs these by hand.</summary>
    public List<string> UnmatchedImportFuels { get; set; } = [];

    /// <summary>Per-row parse problems carried through from the reader.</summary>
    public List<ParseInvoiceIssue> Errors { get; set; } = [];
}

/// <summary>One invoice line, resolved against the import: the cost/liter to pre-fill and whether it found a home.</summary>
public sealed class ParsedInvoiceLineDto
{
    public int Line { get; set; }
    public string InvoiceLabel { get; set; } = null!;

    /// <summary>Cost/liter to pre-fill into the batch-cost input: the invoice's price column, else total ÷ quantity.</summary>
    public decimal? CostPerLiter { get; set; }
    public decimal? Liters { get; set; }
    public decimal? Total { get; set; }

    public bool Matched { get; set; }
    public string? FuelTypeId { get; set; }
    public string? FuelTypeName { get; set; }
    public string? Provider { get; set; }

    /// <summary>Liters this fuel actually holds in the import — a sanity cross-check against the invoice quantity.</summary>
    public decimal? ImportLiters { get; set; }

    /// <summary>Non-fatal note for the operator (cost derived from total, quantity mismatch, no match, …).</summary>
    public string? Warning { get; set; }
}

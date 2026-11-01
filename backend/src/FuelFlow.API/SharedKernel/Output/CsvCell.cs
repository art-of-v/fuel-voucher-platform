namespace FuelFlow.SharedKernel.Output;

/// <summary>
/// The one cell writer behind every CSV this API hands to a human.
/// </summary>
/// <remarks>
/// <para>
/// A cell that STARTS with <c>=</c>, <c>+</c>, <c>-</c> or <c>@</c> is a FORMULA to a spreadsheet,
/// and these files are opened in one - by an accountant, on a machine that has the books next to it.
/// The leading apostrophe is Excel's own "this is text" marker: the cell still shows what was typed
/// and the spreadsheet drops the marker when it reads the value back.
/// </para>
/// <para>
/// A tab or a carriage return is in the same list because a spreadsheet can treat a leading
/// whitespace as a delimiter and re-split the row. The admin panel's own client-side exporter
/// guards exactly these six (<c>admin/src/lib/utils.ts</c>), so the two ends of one file say the
/// same thing.
/// </para>
/// <para>
/// Money does not come through here. A negative amount is a number, and the marker would turn the
/// cell into text the accountant cannot sum - so each file writes its own numbers and only its text
/// goes through this guard.
/// </para>
/// </remarks>
public static class CsvCell
{
    /// <summary>
    /// One text cell: formula marker where one is needed, inner quotes doubled, result quoted.
    /// A null arrives as an empty quoted cell, which is what a spreadsheet reads as "nothing here".
    /// </summary>
    public static string Text(string? value)
    {
        var text = value ?? string.Empty;

        if (text.Length > 0 && (text[0] is '=' or '+' or '-' or '@' or '\t' or '\r'))
            text = "'" + text;

        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}

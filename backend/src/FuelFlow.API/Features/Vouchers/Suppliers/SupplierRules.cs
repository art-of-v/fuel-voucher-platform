using System.Text.RegularExpressions;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FuelFlow.Features.Vouchers.Suppliers;

/// <summary>
/// The rules a supplier row has to satisfy before it is written: the legal forms we accept, the
/// shape of the requisites, and the uniqueness of the name.
/// </summary>
internal static class SupplierRules
{
    /// <summary>
    /// The legal forms we accept. Kept as data rather than a free-text field so the admin UI can offer a
    /// fixed list and a saved supplier always round-trips to a form we know how to render.
    /// </summary>
    public static readonly string[] LegalForms = ["ФОП", "ТОВ", "ТзОВ", "ПАТ", "ФОП-ЄЗ", "Інше"];

    private static readonly Regex EdrIpnPattern = new(@"^\d{8,10}$", RegexOptions.Compiled);
    private static readonly Regex RnkrrPattern = new(@"^\d{5}$", RegexOptions.Compiled);
    // Digits only, with the punctuation people actually type: an optional +, spaces, dashes, dots and
    // parentheses. Nothing else - the point is to refuse prose, not to second-guess a valid number.
    private static readonly Regex PhonePattern = new(@"^\+?[\d\s\-().]{9,20}$", RegexOptions.Compiled);

    /// <summary>
    /// The name is the only mandatory field. The registry code is optional - a supplier can be
    /// recorded before anyone has looked it up - but if it is given it has to be a code.
    /// <para>
    /// Everything else depends on the legal form — an ФОП has an ІПН and an LLC has an ЄДРОПУ —
    /// so rejecting a blank there would reject half the suppliers we actually deal with.
    /// </para>
    /// </summary>
    public static string? Validate(CreateSupplierRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return "Supplier name is required.";

        if (request.LegalForm is { Length: > 0 } form && !LegalForms.Contains(form.Trim()))
            return $"Unknown legal form '{form}'. Expected one of: {string.Join(", ", LegalForms)}.";

        if (!string.IsNullOrWhiteSpace(request.EdrIpn) && !EdrIpnPattern.IsMatch(request.EdrIpn.Trim()))
            return "ЄДРОПУ/ІПН must be 8-10 digits.";

        if (!string.IsNullOrWhiteSpace(request.Rnkrr) && !RnkrrPattern.IsMatch(request.Rnkrr.Trim()))
            return "РНОКР must be 5 digits.";

        if (!string.IsNullOrWhiteSpace(request.Email) && !request.Email.Contains('@'))
            return "Email is not valid.";

        // A phone is what someone dials to reach this counterparty, so refuse prose rather than storing
        // it: this field silently accepted any 32 characters, and free text in it reaches invoices.
        if (!string.IsNullOrWhiteSpace(request.Phone) && !PhonePattern.IsMatch(request.Phone.Trim()))
            return "Phone must be a number: digits with an optional +, spaces, dashes, dots or parentheses.";

        // Case-insensitively accepted and normalised by the caller, because the CHECK constraint is not:
        // 'Partner' would otherwise reach the database and come back as a 23514 with a stack trace in
        // the Error Logs. Rejecting it here names the valid set instead.
        if (!string.IsNullOrWhiteSpace(request.AcquisitionSource)
            && !AcquisitionSources.IsKnown(request.AcquisitionSource.Trim().ToLowerInvariant()))
        {
            return $"Unknown supply route '{request.AcquisitionSource}'. Expected one of: {string.Join(", ", AcquisitionSources.All)}.";
        }

        return null;
    }

    /// <summary>
    /// The registry code, trimmed — and a blank becomes NULL rather than an empty string.
    /// <para>
    /// An empty string is not a registry code, and the database's format constraint would
    /// reject it while NULL passes: a supplier with no code is allowed, a supplier with a
    /// blank "code" is not. That distinction is the whole point of the constraint.
    /// </para>
    /// </summary>
    public static string? NormaliseEdrIpn(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// The unique index on <c>suppliers.name</c> is the real authority; the pre-check in the handlers
    /// only turns its rejection into a readable answer. Two operators saving the same name in the same
    /// instant both clear the pre-check, so the loser still has to be caught here - as the same 409,
    /// not an opaque 500.
    /// </summary>
    public static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}

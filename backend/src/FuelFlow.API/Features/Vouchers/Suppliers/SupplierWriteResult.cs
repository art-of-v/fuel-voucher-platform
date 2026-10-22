namespace FuelFlow.Features.Vouchers.Suppliers;

/// <summary>What a supplier write did.</summary>
public enum SupplierCommandOutcome
{
    /// <summary>The write happened.</summary>
    Success,

    /// <summary>The request was refused before anything was written.</summary>
    Invalid,

    /// <summary>Another supplier already carries that name.</summary>
    NameTaken,

    /// <summary>There is no supplier with that id.</summary>
    NotFound
}

/// <summary>
/// The answer of a supplier write: what happened, the row when there is one, and the reason when
/// there is not.
/// </summary>
public sealed record SupplierWriteResult(
    SupplierCommandOutcome Outcome,
    SupplierDto? Supplier = null,
    string? Error = null,
    string? ConflictCode = null,
    string? ConflictMessage = null)
{
    /// <summary>
    /// The machine-readable half of the duplicate-name answer. Sent with the message so the admin can
    /// localise from the code, the same channel the checkout and renewal rejections use.
    /// </summary>
    public const string NameTakenCode = "supplier_name_taken";

    /// <summary>The write happened. The row is there for every outcome but the deactivation.</summary>
    public static SupplierWriteResult Done(SupplierDto? supplier = null)
        => new(SupplierCommandOutcome.Success, supplier);

    /// <summary>The request was refused, and the reason says what to change.</summary>
    public static SupplierWriteResult Invalid(string error)
        => new(SupplierCommandOutcome.Invalid, Error: error);

    /// <summary>There is no supplier with that id.</summary>
    public static SupplierWriteResult NotFound()
        => new(SupplierCommandOutcome.NotFound);

    /// <summary>The name is taken by another supplier.</summary>
    public static SupplierWriteResult NameTaken(string name) => new(
        SupplierCommandOutcome.NameTaken,
        ConflictCode: NameTakenCode,
        ConflictMessage: $"A supplier named '{name}' already exists.");
}

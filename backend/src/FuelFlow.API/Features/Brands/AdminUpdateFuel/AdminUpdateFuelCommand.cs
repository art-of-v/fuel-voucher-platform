namespace FuelFlow.Features.Brands.AdminUpdateFuel;

/// <summary>
/// The operator's edit of a fuel - in practice a repricing, and sometimes a rename.
/// </summary>
/// <param name="FuelId">The fuel to edit.</param>
/// <param name="Fuel">The fuel as the form filled it in.</param>
/// <param name="UserId">Who asked for it, for the audit trail and for the price history.</param>
/// <param name="UserName">Their name, for the audit trail.</param>
public sealed record AdminUpdateFuelCommand(Guid FuelId, BrandFuelDto Fuel, Guid UserId, string? UserName);

/// <summary>
/// What the edit answered: whether the fuel was repriced, or why it was not.
/// </summary>
/// <param name="Success">Whether the fuel was written.</param>
/// <param name="NotFound">Whether the fuel is not there - a 404.</param>
/// <param name="Conflict">
/// Whether the refusal is a clash with a row that already exists - a 409 - rather than a request
/// that never had a chance - a 400.
/// </param>
/// <param name="Error">The reason, in the words the operator is shown.</param>
/// <param name="Field">
/// The field the refusal is about - the code or the rehearsal flag. It is the field name the form
/// sent, so the screen can put the message next to the input that caused it.
/// </param>
/// <param name="FuelTypeId">
/// The fuel that already owns the code, when the refusal is about one. Null when the refusal names
/// no fuel - and the body must not grow a null field where it never had one.
/// </param>
public sealed record AdminUpdateFuelResult
{
    public bool Success { get; init; }
    public bool NotFound { get; init; }
    public bool Conflict { get; init; }
    public string? Error { get; init; }
    public string? Field { get; init; }
    public Guid? FuelTypeId { get; init; }
}

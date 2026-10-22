namespace FuelFlow.Features.Brands.AdminAddFuel;

/// <summary>
/// The operator's request to put a fuel into a network's catalogue.
/// </summary>
/// <param name="BrandId">The network the fuel belongs to.</param>
/// <param name="Fuel">The fuel as the form filled it in.</param>
/// <param name="UserId">Who asked for it, for the audit trail and for the price history.</param>
/// <param name="UserName">Their name, for the audit trail.</param>
public sealed record AdminAddFuelCommand(Guid BrandId, CreateFuelRequest Fuel, Guid UserId, string? UserName);

/// <summary>
/// What the add answered: the fuel that was created, or the reason it was refused.
/// </summary>
/// <param name="Success">Whether the fuel was created.</param>
/// <param name="NotFound">Whether the network it was to belong to is not there - a 404.</param>
/// <param name="Conflict">
/// Whether the refusal is a clash with a row that already exists - a 409 - rather than a request
/// that never had a chance - a 400.
/// </param>
/// <param name="Error">The reason, in the words the operator is shown.</param>
/// <param name="Field">
/// The field the refusal is about - the name, the code or the rehearsal flag. It is the field name
/// the form sent, so the screen can put the message next to the input that caused it.
/// </param>
/// <param name="FuelTypeId">
/// The fuel that already owns the name or the code, when the refusal is about one. Null when the
/// refusal names no fuel - and the body must not grow a null field where it never had one.
/// </param>
/// <param name="CreatedFuelId">The fuel that was created, when it was.</param>
public sealed record AdminAddFuelResult
{
    public bool Success { get; init; }
    public bool NotFound { get; init; }
    public bool Conflict { get; init; }
    public string? Error { get; init; }
    public string? Field { get; init; }
    public Guid? FuelTypeId { get; init; }
    public Guid CreatedFuelId { get; init; }
}

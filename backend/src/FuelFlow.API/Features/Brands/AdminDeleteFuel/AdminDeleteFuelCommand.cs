namespace FuelFlow.Features.Brands.AdminDeleteFuel;

/// <summary>
/// The operator's request to archive a fuel.
/// </summary>
/// <param name="FuelId">The fuel to archive.</param>
/// <param name="UserId">Who asked for it, for the audit trail.</param>
/// <param name="UserName">Their name, for the audit trail.</param>
public sealed record AdminDeleteFuelCommand(Guid FuelId, Guid UserId, string? UserName);

/// <summary>
/// One of the vouchers that still hold value against the fuel, and the one the operator is shown
/// when the archive is refused.
/// </summary>
/// <param name="VoucherNumber">The number on the paper, so the operator can find it.</param>
/// <param name="Status">The voucher's state, as its name.</param>
/// <param name="Liters">What the voucher is worth, in litres.</param>
public sealed record LiveVoucherExample(string VoucherNumber, string Status, decimal Liters);

/// <summary>
/// What the archive answered: whether the fuel was archived, or why it could not be.
/// </summary>
/// <param name="Success">Whether the fuel was archived.</param>
/// <param name="NotFound">Whether the fuel is not there - a 404.</param>
/// <param name="Conflict">
/// Whether the archive was refused because the fuel still has vouchers against it - a 409.
/// </param>
/// <param name="Error">The reason, in the words the operator is shown.</param>
/// <param name="Code">The stable machine code for the refusal, when there is one.</param>
/// <param name="ActiveVoucherCount">
/// How many vouchers still hold value against the fuel. The count, not the examples: the operator
/// needs to know the size of the problem and not only the first few of it.
/// </param>
/// <param name="Examples">A few of the vouchers that block the archive.</param>
public sealed record AdminDeleteFuelResult
{
    public bool Success { get; init; }
    public bool NotFound { get; init; }
    public bool Conflict { get; init; }
    public string? Error { get; init; }
    public string? Code { get; init; }
    public int ActiveVoucherCount { get; init; }
    public List<LiveVoucherExample> Examples { get; init; } = [];
}

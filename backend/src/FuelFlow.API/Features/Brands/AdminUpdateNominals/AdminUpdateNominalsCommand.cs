namespace FuelFlow.Features.Brands.AdminUpdateNominals;

/// <summary>
/// The operator's edit of the sizes a network sells its fuels in.
/// </summary>
/// <param name="BrandId">The network whose sizes are being edited.</param>
/// <param name="Nominals">The sizes, in litres, that every fuel of the network is sold in.</param>
/// <param name="UserId">Who asked for it, for the audit trail.</param>
/// <param name="UserName">Their name, for the audit trail.</param>
public sealed record AdminUpdateNominalsCommand(Guid BrandId, List<int> Nominals, Guid UserId, string? UserName);

/// <summary>
/// What the edit answered: whether the sizes were written, or why they were not.
/// </summary>
/// <param name="Success">Whether the sizes were written.</param>
/// <param name="NotFound">Whether the network is not there - a 404.</param>
/// <param name="Error">The reason, in the words the operator is shown.</param>
/// <param name="Invalid">
/// The sizes that cannot exist - the ones that are not positive numbers of litres. They are named
/// rather than refused as a whole, so the operator sees which of the values was the wrong one.
/// </param>
public sealed record AdminUpdateNominalsResult
{
    public bool Success { get; init; }
    public bool NotFound { get; init; }
    public string? Error { get; init; }
    public List<int> Invalid { get; init; } = [];
}

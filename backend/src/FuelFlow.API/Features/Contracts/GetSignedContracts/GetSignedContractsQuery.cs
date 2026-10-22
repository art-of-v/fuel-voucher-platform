namespace FuelFlow.Features.Contracts.GetSignedContracts;

/// <summary>
/// The list of signatures on the platform - a page of it.
/// </summary>
/// <param name="Page">
/// 1-based page number. Clamped by <see cref="FuelFlow.Persistence.PageLimits.ClampPage"/>.
/// </param>
/// <param name="Limit">
/// Rows per page. Clamped by <see cref="FuelFlow.Persistence.PageLimits.ClampPageSize"/>, so one
/// request cannot ask for every signature the platform has ever collected.
/// </param>
public sealed record GetSignedContractsQuery(int Page = 1, int Limit = 50);

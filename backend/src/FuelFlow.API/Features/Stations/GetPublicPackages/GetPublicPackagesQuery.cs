namespace FuelFlow.Features.Brands.GetPublicPackages;

/// <param name="CallerUserId">
/// The signed-in caller's id, or null for an anonymous visitor. Not carried in the JWT —
/// <c>users.is_qa_account</c> lives in the database and is deliberately re-read per request, so
/// revoking the flag takes effect immediately rather than at the next token refresh. A QA account is
/// only ever served emulated test vouchers (<c>fuel_vouchers.data_kind</c>), which makes "is this in
/// stock" two different questions for a QA account and a real customer.
/// </param>
public sealed record GetPublicPackagesQuery(Guid? CallerUserId);
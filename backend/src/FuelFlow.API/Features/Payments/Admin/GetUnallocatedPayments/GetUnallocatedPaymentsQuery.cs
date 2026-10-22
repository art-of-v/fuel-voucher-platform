namespace FuelFlow.Features.Payments.Admin.GetUnallocatedPayments;

/// <summary>
/// The worklist of money the platform holds and is not yet against a sale.
/// </summary>
/// <param name="Page">Which page of the worklist, counting from one.</param>
/// <param name="Limit">How many rows a page carries. The handler bounds it.</param>
public sealed record GetUnallocatedPaymentsQuery(int Page, int Limit);

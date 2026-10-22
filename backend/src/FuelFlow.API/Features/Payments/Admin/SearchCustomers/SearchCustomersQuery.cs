namespace FuelFlow.Features.Payments.Admin.SearchCustomers;

/// <summary>
/// The customer picker: a bounded search over the customers by name, phone or settlement code.
/// </summary>
/// <param name="Search">What the operator typed.</param>
/// <param name="Limit">How many hits the picker may show. The handler bounds it.</param>
public sealed record SearchCustomersQuery(string? Search, int Limit);

using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Payments.Admin.GetCustomerByCode;

/// <summary>
/// Finds the customer a settlement code names, or says plainly that it names nobody.
/// </summary>
/// <remarks>
/// The workflow this exists for: finance typed a code that did not match, and the operator needs
/// to know whether that is a typo in a code that does exist, or a code for a customer who does not.
/// Those are different problems with different fixes, and "not found" alone does not tell them
/// which. Deliberately no name or phone search — that is a customer list with its own pagination,
/// and a partial version of it here would be a worse version of one that already exists.
/// </remarks>
public sealed class GetCustomerByCodeQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetCustomerByCodeQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<CustomerByCodeResult> HandleAsync(
        GetCustomerByCodeQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalised = (query.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (normalised.Length != 8)
            return CustomerByCodeResult.Malformed("A settlement code is eight characters.");

        var customer = await _context.Users
            .AsNoTracking()
            .Where(u => u.SettlementCode == normalised)
            .Select(u => new { u.Id, u.PhoneNumber, u.FirstName, u.LastName, u.SettlementCode })
            .FirstOrDefaultAsync(cancellationToken);

        if (customer is null)
        {
            return CustomerByCodeResult.NotFound(
                $"No customer has the code {normalised}. It was either mistyped, or it belongs to a customer who has not registered yet.");
        }

        return CustomerByCodeResult.Found(new CustomerSearchHit(
            customer.Id,
            customer.PhoneNumber,
            customer.SettlementCode,
            string.IsNullOrEmpty(customer.FirstName) && string.IsNullOrEmpty(customer.LastName)
                ? null
                : (customer.FirstName + " " + customer.LastName).Trim()));
    }
}

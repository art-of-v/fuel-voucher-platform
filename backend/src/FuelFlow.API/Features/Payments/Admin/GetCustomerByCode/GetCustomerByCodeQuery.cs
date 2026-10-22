namespace FuelFlow.Features.Payments.Admin.GetCustomerByCode;

/// <summary>
/// The customer a settlement code names.
/// </summary>
/// <param name="Code">The code as the operator typed it.</param>
public sealed record GetCustomerByCodeQuery(string? Code);

/// <summary>
/// The answer to a settlement-code lookup: the customer it names, or the reason it names nobody.
/// </summary>
/// <remarks>
/// The two refusals are different problems with different fixes, so they are told apart here
/// rather than collapsed into one "not found": a code that cannot be a code is a typo in the
/// form, and a code that is well formed but unknown is either a typo in a code that does exist
/// or a customer who has not registered yet.
/// </remarks>
/// <param name="CodeIsValid">Whether the code could have named anybody at all.</param>
/// <param name="Customer">The customer the code names, when it names one.</param>
/// <param name="Error">Why the code named nobody, when it named nobody.</param>
public sealed record CustomerByCodeResult(bool CodeIsValid, CustomerSearchHit? Customer, string? Error)
{
    public static CustomerByCodeResult Malformed(string error) => new(false, null, error);

    public static CustomerByCodeResult NotFound(string error) => new(true, null, error);

    public static CustomerByCodeResult Found(CustomerSearchHit customer) => new(true, customer, null);
}

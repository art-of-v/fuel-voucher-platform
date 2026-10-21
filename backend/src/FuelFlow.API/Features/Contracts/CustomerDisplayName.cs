namespace FuelFlow.Features.Contracts;

/// <summary>
/// The name a customer is shown under when they have one, and their phone number when they
/// do not.
/// </summary>
/// <remarks>
/// A customer with no name is still a customer: the phone number is what identifies them
/// everywhere else in the admin, and a blank cell in the contracts list would make the row
/// unattributable - an operator looking at a contract would not be able to say whose it is.
/// </remarks>
public static class CustomerDisplayName
{
    /// <summary>
    /// The customer's name, or their phone number, or nothing when they have neither.
    /// </summary>
    /// <param name="firstName">The customer's first name, if they gave one.</param>
    /// <param name="lastName">The customer's last name, if they gave one.</param>
    /// <param name="phoneNumber">The customer's phone number.</param>
    public static string? Of(string? firstName, string? lastName, string? phoneNumber)
    {
        var name = $"{firstName} {lastName}".Trim();
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber;
    }
}

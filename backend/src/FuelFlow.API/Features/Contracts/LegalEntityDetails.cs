using FuelFlow.Features.Contracts.SharedModels;

namespace FuelFlow.Features.Contracts;

/// <summary>
/// The details a legal-entity write carries, and the gate they pass before anything is written.
/// </summary>
/// <remarks>
/// One gate for the three write paths - the profile upsert, the create and the scoped edit -
/// because they ask the same question about the same fields, and a rule written three times is a
/// rule that is changed twice.
/// </remarks>
/// <param name="Name">The company's name.</param>
/// <param name="Edrpou">The company's registry code.</param>
/// <param name="VatNumber">The company's VAT number, when it has one.</param>
/// <param name="Address">The company's address.</param>
/// <param name="DirectorName">The name of the company's director.</param>
/// <param name="Phone">The company's phone number.</param>
/// <param name="Email">The company's email address.</param>
public sealed record LegalEntityDetails(
    string Name,
    string Edrpou,
    string? VatNumber,
    string? Address,
    string? DirectorName,
    string? Phone,
    string? Email)
{
    /// <summary>
    /// The problem with these details, or <c>null</c> when they are acceptable.
    /// </summary>
    public string? DescribeProblem()
    {
        if (string.IsNullOrWhiteSpace(Name))
            return "Name is required";

        if (string.IsNullOrWhiteSpace(Edrpou))
            return "EDRPOU is required";

        // Format gate, not just non-empty: a 16-digit typo is not an EDRPOU (#128).
        var edrpouProblem = LegalEntityIdentifier.DescribeEdrpouProblem(Edrpou);
        if (edrpouProblem != null)
            return edrpouProblem;

        return LegalEntityIdentifier.DescribeVatProblem(VatNumber);
    }
}

namespace FuelFlow.Features.Brands;

/// <summary>
/// The sizes a fuel is sold in when nobody has said which sizes it should have.
/// </summary>
/// <remarks>
/// The list lives here rather than in each of the two write paths that use it, because the two have
/// to agree: a fuel added through the add-fuel screen and a fuel seeded by the price edit are both
/// "a fuel with no sizes yet", and two lists that drifted apart would leave the catalogue with
/// sizes that appear from nowhere and sizes that quietly vanish.
/// </remarks>
internal static class DefaultNominalVolumes
{
    /// <summary>The sizes, in litres.</summary>
    public static readonly List<int> Liters = [2, 3, 5, 10, 20, 50];
}

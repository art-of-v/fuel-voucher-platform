namespace FuelFlow.SharedKernel.Options;

public sealed class AppVersionOptions
{
    public const string SectionName = "AppVersion";

    public string MinimumVersion { get; set; } = "1.0.0";
    public string IosStoreUrl { get; set; } = string.Empty;
    public string AndroidStoreUrl { get; set; } = string.Empty;
}

namespace FuelFlow.SharedKernel.Options;

public sealed class DeviceAuthOptions
{
    public const string SectionName = "DeviceAuth";
    public bool Enabled { get; set; } = false;

    public int ChallengeExpirySeconds { get; set; } = 30;

    public int SignatureNonceTtlSeconds { get; set; } = 60;

    public int TimestampToleranceMs { get; set; } = 300000;

    public bool AllowDevelopmentBypass { get; set; } = true;

    // The real mobile checkout routes (PurchaseController). Keep in sync with
    // the mobile app's SIGNATURE_REQUIRED_ENDPOINTS list.
    public List<string> RequireSignatureForEndpoints { get; set; } = new()
    {
        "/api/purchases",
        "/api/purchases/bulk"
    };
}

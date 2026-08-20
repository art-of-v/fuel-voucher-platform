namespace FuelFlow.SharedKernel.Options;

public sealed class DeviceAuthOptions
{
    public const string SectionName = "DeviceAuth";
    public bool Enabled { get; set; } = false;

    public int ChallengeExpirySeconds { get; set; } = 30;

    // Nonce TTL must be >= TimestampToleranceMs/1000, otherwise a consumed
    // signed request can be replayed once its nonce expires from cache but
    // its timestamp is still inside the tolerance window.
    public int SignatureNonceTtlSeconds { get; set; } = 300;

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

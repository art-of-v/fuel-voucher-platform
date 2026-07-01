namespace FuelFlow.Options;

public sealed class DeviceAuthOptions
{
    public const string SectionName = "DeviceAuth";
    public bool Enabled { get; set; } = false;

    public int ChallengeExpirySeconds { get; set; } = 30;

    public int SignatureNonceTtlSeconds { get; set; } = 60;

    public int TimestampToleranceMs { get; set; } = 300000;

    public bool AllowDevelopmentBypass { get; set; } = true;

    public List<string> RequireSignatureForEndpoints { get; set; } = new()
    {
        "/api/orders/checkout",
        "/api/auth/device/*"
    };
}

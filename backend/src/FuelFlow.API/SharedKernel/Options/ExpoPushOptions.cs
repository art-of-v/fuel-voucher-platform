namespace FuelFlow.SharedKernel.Options;

public sealed class ExpoPushOptions
{
    public const string SectionName = "ExpoPush";

    /// <summary>
    /// Expo Push send endpoint. Public and credential-free by default: delivery integrity is
    /// enforced by APNs/FCM, not the transport, so no secret is required to POST here.
    /// </summary>
    public string BaseUrl { get; set; } = "https://exp.host/--/api/v2/push/send";

    /// <summary>
    /// Optional Expo access token. Only needed if "Enhanced Security for Push Notifications" is
    /// enabled on the Expo account; left blank otherwise (the default project configuration).
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>
    /// Master switch. When false the sender no-ops — in-app notifications are still written, only
    /// the outbound push is skipped. Lets a deploy turn push off without a code change.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

namespace FuelFlow.Infra;

/// <summary>
/// Configuration for FuelFlow infrastructure stack.
/// </summary>
public record FuelFlowStackConfig
{
    // ===== Fixed Values (decided during setup) =====

    /// <summary>
    /// Environment name - production only for now
    /// </summary>
    public string Environment { get; init; } = "prod";

    /// <summary>
    /// Azure region for resources
    /// </summary>
    public string Location { get; init; } = "West Europe";

    /// <summary>
    /// Resource naming prefix
    /// </summary>
    public string Prefix { get; init; } = "fuelflow";

    /// <summary>
    /// PostgreSQL administrator login
    /// </summary>
    public string PostgresAdminLogin { get; init; } = "fuelflow_admin";

    /// <summary>
    /// App Service Plan SKU (B1 = Basic, ~$13/month)
    /// </summary>
    public string AppServiceSkuName { get; init; } = "B1";

    /// <summary>
    /// PostgreSQL storage size in MB (32 GB)
    /// </summary>
    public int PostgresStorageMb { get; init; } = 32768;

    /// <summary>
    /// Redis SKU (Basic = no SLA, ~$13/month)
    /// </summary>
    public string RedisSkuName { get; init; } = "Basic";

    /// <summary>
    /// Redis capacity (0 = 250MB for Basic tier)
    /// </summary>
    public int RedisCapacity { get; init; } = 0;

    /// <summary>
    /// Log retention in days
    /// </summary>
    public int LogRetentionDays { get; init; } = 30;

    /// <summary>
    /// IP addresses allowed for PostgreSQL (comma-separated, or 0.0.0.0 = allow all)
    /// </summary>
    public string AllowedIpAddresses { get; init; } = "0.0.0.0";

    /// <summary>
    /// Storage account replication type (LRS = cheapest)
    /// </summary>
    public string StorageReplicationType { get; init; } = "LRS";

    // ===== Secrets (from environment variables) =====

    /// <summary>
    /// PostgreSQL administrator password
    /// </summary>
    public required string PostgresAdminPassword { get; init; }

    /// <summary>
    /// Stripe Secret Key
    /// </summary>
    public required string StripeSecretKey { get; init; }

    /// <summary>
    /// Stripe Publishable Key (not secret, but managed here for consistency)
    /// </summary>
    public required string StripePublishableKey { get; init; }

    /// <summary>
    /// Stripe Webhook Secret
    /// </summary>
    public required string StripeWebhookSecret { get; init; }

    /// <summary>
    /// Google Gemini API Key (for OCR)
    /// </summary>
    public required string GeminiApiKey { get; init; }

    /// <summary>
    /// Twilio Account SID
    /// </summary>
    public required string TwilioAccountSid { get; init; }

    /// <summary>
    /// Twilio Auth Token
    /// </summary>
    public required string TwilioAuthToken { get; init; }

    /// <summary>
    /// Twilio Phone Number (SMS sender)
    /// </summary>
    public required string TwilioPhoneNumber { get; init; }

    /// <summary>
    /// Express Session Secret
    /// </summary>
    public required string SessionSecret { get; init; }

    /// <summary>
    /// QR Code Encryption Key (for voucher data)
    /// </summary>
    public required string QrEncryptionKey { get; init; }

    /// <summary>
    /// Email address for alerts
    /// </summary>
    public required string AlertEmail { get; init; }

    // ===== Computed Properties =====

    /// <summary>
    /// Resource prefix with environment
    /// </summary>
    public string ResourcePrefix => $"{Prefix}-{Environment}";

    /// <summary>
    /// Storage account name (must be globally unique, lowercase, no dashes)
    /// </summary>
    public string StorageAccountName => $"{Prefix}{Environment}storage".ToLowerInvariant();

    /// <summary>
    /// Key Vault name (must be globally unique)
    /// </summary>
    public string KeyVaultName => $"{Prefix}{Environment}kv";

    /// <summary>
    /// Tags for all resources
    /// </summary>
    public Dictionary<string, string> Tags => new()
    {
        ["Environment"] = Environment,
        ["Project"] = "FuelFlow",
        ["ManagedBy"] = "CDKTF"
    };
}

using FuelFlow.Infra;

App app = new();

// All secrets must be provided via environment variables
// Fixed configuration values are in FuelFlowStackConfig with sensible defaults
new FuelFlowStack(app, "fuelflow-prod", new FuelFlowStackConfig
{
    // Database
    PostgresAdminPassword = GetRequiredEnv("POSTGRES_ADMIN_PASSWORD"),

    // Stripe
    StripeSecretKey = GetRequiredEnv("STRIPE_SECRET_KEY"),
    StripePublishableKey = GetRequiredEnv("STRIPE_PUBLISHABLE_KEY"),
    StripeWebhookSecret = GetRequiredEnv("STRIPE_WEBHOOK_SECRET"),

    // Gemini AI (Google)
    GeminiApiKey = GetRequiredEnv("GEMINI_API_KEY"),

    // Twilio SMS
    TwilioAccountSid = GetRequiredEnv("TWILIO_ACCOUNT_SID"),
    TwilioAuthToken = GetRequiredEnv("TWILIO_AUTH_TOKEN"),
    TwilioPhoneNumber = GetRequiredEnv("TWILIO_PHONE_NUMBER"),

    // Application Secrets
    SessionSecret = GetRequiredEnv("SESSION_SECRET"),
    QrEncryptionKey = GetRequiredEnv("QR_ENCRYPTION_KEY"),

    // Alerts
    AlertEmail = GetRequiredEnv("ALERT_EMAIL")
});

app.Synth();
Console.WriteLine("CDKTF synth completed successfully.");
Console.WriteLine("Run 'cdktf deploy' to apply infrastructure changes.");

static string GetRequiredEnv(string name) =>
    Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"Environment variable '{name}' is required. Please set it before running CDKTF.");

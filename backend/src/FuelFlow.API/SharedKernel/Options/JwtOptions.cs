namespace FuelFlow.SharedKernel.Options;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int AccessTokenExpirationMinutes { get; set; } = 15;
    // 14 days matches the mobile-session inactivity window (spec §8). The window slides:
    // each refresh mints a token expiring 14 days from now, so continued use keeps a
    // session alive while 14 days of inactivity lets it lapse.
    public int RefreshTokenExpirationDays { get; set; } = 14;
}

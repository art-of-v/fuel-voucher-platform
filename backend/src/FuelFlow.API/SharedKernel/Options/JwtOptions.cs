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

    // Reuse-detection grace, in seconds (#26). A refresh token that was rotated but whose
    // response never reached the client - a dropped connection, or the app being suspended
    // or killed mid-flight - gets replayed by the client on its next attempt. On the wire
    // that is indistinguishable from a stolen-token replay, but it has a narrow, self-limiting
    // shape: the replay lands within this many seconds of the rotation AND the family still
    // has exactly one live successor (the client never advanced past it because it never
    // received it). In that shape only, the replay is treated as a benign lost-rotation and
    // re-rotated instead of revoking the whole family. Outside it the family is revoked as
    // before. The window re-arms once it elapses, so a genuine replay attack is still caught
    // and the security exposure is bounded to this period. Set to 0 to disable and fall back
    // to strict reuse detection (any replay revokes the family).
    public int RefreshReuseGraceSeconds { get; set; } = 60;
}

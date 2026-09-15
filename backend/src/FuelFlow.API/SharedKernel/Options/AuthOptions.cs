namespace FuelFlow.SharedKernel.Options;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>The Role.Name that identifies a dashboard administrator. Kept here
    /// so OTP channel selection matches the value [Authorize(Roles = ...)] expects.</summary>
    public const string AdminRoleName = "Admin";

    public bool DevBypass { get; set; } = false;

    /// <summary>
    /// When true (default), admin-role users receive their sign-in OTP by email over the
    /// configured support SMTP account instead of a paid SMS. Delivery falls back to SMS
    /// whenever email is unconfigured, the account has no email, or the send throws, so an
    /// admin can never be locked out by the email path. Set Auth__AdminOtpViaEmail=false to
    /// force SMS for everyone.
    /// </summary>
    public bool AdminOtpViaEmail { get; set; } = true;
}

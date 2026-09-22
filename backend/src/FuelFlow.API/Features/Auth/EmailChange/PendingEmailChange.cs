using System.Net.Mail;
using System.Security.Cryptography;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Security;

namespace FuelFlow.Features.Auth.EmailChange;

/// <summary>
/// Shared mechanics of a verified email change, used by both the admin path
/// (SetUserEmailCommandHandler) and the self-service path (UpdateUserCommandHandler): validate the
/// address, stage it as a pending change on the user (active email untouched), and build the
/// confirmation link that the public GET /api/auth/email/confirm endpoint completes. Only the
/// token hash is stored; the raw token lives only in the emailed link.
/// </summary>
public static class PendingEmailChange
{
    public static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    public static bool IsValidEmail(string value)
    {
        try
        {
            var addr = new MailAddress(value);
            return addr.Address == value && value.Contains('.');
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Stages <paramref name="newEmail"/> as a pending change on <paramref name="user"/> and returns
    /// the raw one-time token to embed in the confirmation link. Does NOT touch the active email.
    /// </summary>
    public static string Begin(User user, string newEmail)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        user.PendingEmail = newEmail;
        user.PendingEmailTokenHash = SecretsHasher.Hash(token);
        user.PendingEmailExpiresAtUtc = DateTime.UtcNow.Add(Ttl);
        user.UpdatedAtUtc = DateTime.UtcNow;
        return token;
    }

    public static string BuildConfirmUrl(string confirmBaseUrl, string token) =>
        $"{confirmBaseUrl.TrimEnd('/')}/api/auth/email/confirm?token={token}";

    public static string BuildConfirmationBody(string confirmUrl) =>
        "A change to this email address was requested for your FuelFlow account.\n\n" +
        "If this was you (or your administrator), confirm it by opening this link:\n" +
        confirmUrl + "\n\n" +
        "The link expires in 24 hours. If you did not expect this, you can ignore this email.";
}

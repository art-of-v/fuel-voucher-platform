namespace FuelFlow.Features.Auth.TwoFactor;

/// <summary>
/// The challenge the client receives when a staff sign-in has proved its first factor and is
/// waiting for the second one.
/// </summary>
/// <param name="Status">
/// "required" - the account already has a secret; the client has to send a code from the
/// authenticator app. "enroll" - the account has no secret yet; the client has to enrol one
/// with the secret it receives and then prove it.
/// </param>
/// <param name="Challenge">The opaque token that ties this attempt to the verified first factor.</param>
/// <param name="EnrollmentSecret">The secret to enrol, when the status is "enroll".</param>
/// <param name="ProvisioningUri">The otpauth:// URI to scan, when the status is "enroll".</param>
public sealed record TwoFactorChallengeResponse(
    string Status,
    string Challenge,
    string? EnrollmentSecret,
    string? ProvisioningUri);

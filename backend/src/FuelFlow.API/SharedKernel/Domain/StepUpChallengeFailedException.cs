namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// Raised when a step-up re-authentication challenge (a fresh OTP proving control of the current
/// account) is missing, wrong or expired. Distinct from <see cref="UnauthorizedAccessException"/>
/// on purpose: the caller's SESSION is valid — it is the step-up factor that failed — so this maps
/// to 403, not 401. The mobile API client force-refreshes and then clears tokens on a 401, which
/// would log a user out for mistyping the code; a 403 carries the stable <see cref="Code"/> instead
/// so the client can show "invalid code" and let them retry. The message is deliberately generic
/// (no oracle about whether the code was absent, wrong or expired).
/// </summary>
public sealed class StepUpChallengeFailedException : Exception
{
    public const string Code = "step_up_failed";

    public StepUpChallengeFailedException()
        : base("Invalid or expired verification code.")
    {
    }
}

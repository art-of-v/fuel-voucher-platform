namespace FuelFlow.Features.Auth.VerifyChallenge;

public sealed class VerifyChallengeCommand
{
    public string DeviceId { get; set; } = string.Empty;
    public string Challenge { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
}

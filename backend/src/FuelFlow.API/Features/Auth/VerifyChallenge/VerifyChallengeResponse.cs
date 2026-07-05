namespace FuelFlow.API.Features.Auth.VerifyChallenge
{
    public sealed class VerifyChallengeResponse
    {
        public bool IsValid { get; set; }
        public Guid? UserId { get; set; }
        public string? Error { get; set; }
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public int ExpiresIn { get; set; }
    }
}

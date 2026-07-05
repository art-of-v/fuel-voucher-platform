namespace FuelFlow.API.Features.Auth.GenerateChallenge
{
    public sealed class GenerateChallengeResponse
    {
        public string Challenge { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
    }
}

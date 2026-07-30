namespace FuelFlow.Features.Auth;

public sealed class VerifyRawRequest
{
    public string Challenge { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
}

namespace FuelFlow.Api.Features.PhoneVerification;

public record PhoneVerification
{
    public int Id { get; init; }
    public string Phone { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }
    public int Verified { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record SendCodeRequest
{
    public string Phone { get; init; } = string.Empty;
}

public record VerifyCodeRequest
{
    public string Phone { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
}

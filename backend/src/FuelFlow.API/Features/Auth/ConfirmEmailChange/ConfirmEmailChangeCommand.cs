namespace FuelFlow.Features.Auth.ConfirmEmailChange;

public sealed record ConfirmEmailChangeCommand(string Token);

public enum ConfirmEmailChangeStatus
{
    Confirmed,
    InvalidToken,
    Expired,
}

public sealed record ConfirmEmailChangeResult(ConfirmEmailChangeStatus Status);

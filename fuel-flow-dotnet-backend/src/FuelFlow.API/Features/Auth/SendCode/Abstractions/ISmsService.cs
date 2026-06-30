namespace FuelFlow.Features.Auth.SendCode.Abstractions;

public interface ISmsService
{
    Task SendVerificationCodeAsync(string phoneNumber, string code, CancellationToken cancellationToken);
}

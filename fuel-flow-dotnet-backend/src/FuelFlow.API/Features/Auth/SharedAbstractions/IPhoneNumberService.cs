namespace FuelFlow.Features.Auth.SharedAbstractions;

public interface IPhoneNumberService
{
    string Normalize(string phoneNumber);
}

namespace FuelFlow.SharedKernel.Abstractions;

public interface IPhoneNumberService
{
    string Normalize(string phoneNumber);
}

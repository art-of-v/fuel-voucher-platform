using System.Text.RegularExpressions;
using FuelFlow.Features.Auth.SharedAbstractions;

namespace FuelFlow.Features.Auth.SharedServices;

public sealed class PhoneNumberService : IPhoneNumberService
{
    public string Normalize(string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new ArgumentException("Phone number cannot be empty", nameof(phoneNumber));
        }

        var cleaned = Regex.Replace(phoneNumber, @"[\s\-\(\)\.]", string.Empty);

        if (cleaned.StartsWith('+'))
        {
            return cleaned;
        }

        if (cleaned.Length == 10)
        {
            return $"+1{cleaned}";
        }

        if (cleaned.Length == 11 && cleaned.StartsWith('1'))
        {
            return $"+{cleaned}";
        }

        if (cleaned.Length > 10 && !cleaned.StartsWith('+'))
        {
            return $"+{cleaned}";
        }

        throw new ArgumentException($"Invalid phone number format: {phoneNumber}", nameof(phoneNumber));
    }
}

namespace FuelFlow.Features.Users.UpdateUser;

public sealed record UpdateUserResponse(
    Guid Id,
    string Phone,
    string? Email,
    string? FirstName,
    string? LastName,
    DateOnly? Birthdate,
    string? ProfileImageUrl,
    string? ReferralCode,
    int BonusBalance
);

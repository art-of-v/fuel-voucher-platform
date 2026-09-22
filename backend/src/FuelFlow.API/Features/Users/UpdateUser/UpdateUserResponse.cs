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
    int BonusBalance,
    // True when the request included a new email: it is NOT applied here but held pending until
    // the user confirms the link sent to the new address (Email above is still the current one).
    bool EmailChangePending = false
);

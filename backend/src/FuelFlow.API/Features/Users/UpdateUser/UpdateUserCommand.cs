namespace FuelFlow.Features.Users.UpdateUser;

public sealed record UpdateUserCommand(
    string UserId,
    string? Email,
    string? FirstName,
    string? LastName,
    DateOnly? Birthdate,
    string? ProfileImageUrl,
    // Public origin the email-confirmation link is built from (e.g. https://api.palne.shop),
    // taken from the request. Only used when Email is a change.
    string ConfirmBaseUrl = ""
);

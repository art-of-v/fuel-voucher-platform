namespace FuelFlow.Features.Users.UpdateUser;

public sealed record UpdateUserCommand(
    string UserId,
    string? Email,
    string? FirstName,
    string? LastName,
    DateOnly? Birthdate,
    string? ProfileImageUrl
);

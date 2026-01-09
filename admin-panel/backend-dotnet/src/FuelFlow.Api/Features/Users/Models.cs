namespace FuelFlow.Api.Features.Users;

public record User
{
    public string Id { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? ProfileImageUrl { get; init; }
    public string? VehicleMake { get; init; }
    public string? VehicleModel { get; init; }
    public string? VehiclePlate { get; init; }
    public string? VehicleFuelType { get; init; }
    public string? ReferralCode { get; init; }
    public string? ReferredBy { get; init; }
    public int BonusBalance { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public record CreateUserRequest
{
    public string? Phone { get; init; }
    public string? Email { get; init; }
}

public record UpdateUserRequest
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? VehicleMake { get; init; }
    public string? VehicleModel { get; init; }
    public string? VehiclePlate { get; init; }
    public string? VehicleFuelType { get; init; }
}

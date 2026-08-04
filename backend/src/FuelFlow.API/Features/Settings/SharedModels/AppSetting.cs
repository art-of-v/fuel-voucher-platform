namespace FuelFlow.Features.Settings.SharedModels;

public sealed class AppSetting
{
    public string Key { get; set; } = null!;
    public string Value { get; set; } = null!;
    public DateTime UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public string? UpdatedByUserName { get; set; }
}

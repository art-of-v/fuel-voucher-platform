namespace FuelFlow.SharedKernel.Options;

public sealed class SmsClubOptions
{
    public const string SectionName = "SmsClub";
    public string Token { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://im.smsclub.mobi";
}
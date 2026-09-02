namespace FuelFlow.SharedKernel.Options;

public sealed class SmsOptions
{
    public const string SectionName = "Sms";
    public const int DefaultDailySendLimit = 500;
    public int DailySendLimit { get; set; } = DefaultDailySendLimit;
}
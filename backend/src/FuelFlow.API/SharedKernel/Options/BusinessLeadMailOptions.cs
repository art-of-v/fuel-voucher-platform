namespace FuelFlow.SharedKernel.Options;

public sealed class BusinessLeadMailOptions
{
    public const string SectionName = "BusinessLeadMail";

    /// <summary>Sales inbox that receives a notification for each persisted lead.</summary>
    public string ToEmail { get; set; } = string.Empty;
}

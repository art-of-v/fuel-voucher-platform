namespace FuelFlow.Features.BusinessLeads.UpdateBusinessLeadStatus;

public sealed record UpdateBusinessLeadStatusCommand(
    IReadOnlyList<Guid> Ids,
    string Status,
    Guid? ActingUserId,
    string? ActingUserName);

public sealed record UpdateBusinessLeadStatusResult(string Status, int Affected, string? Error)
{
    public static UpdateBusinessLeadStatusResult NoIds() =>
        new("NoIds", 0, "No business lead ids supplied");

    public static UpdateBusinessLeadStatusResult UnknownStatus(string status) =>
        new("UnknownStatus", 0, $"Unknown status '{status}'.");

    public static UpdateBusinessLeadStatusResult Unauthenticated() =>
        new("Unauthenticated", 0, null);

    public static UpdateBusinessLeadStatusResult Done(int affected) =>
        new("Done", affected, null);
}

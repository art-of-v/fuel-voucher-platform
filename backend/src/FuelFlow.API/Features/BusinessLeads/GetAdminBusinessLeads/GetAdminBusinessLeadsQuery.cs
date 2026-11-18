namespace FuelFlow.Features.BusinessLeads.GetAdminBusinessLeads;

public sealed record GetAdminBusinessLeadsQuery(
    int Offset = 0,
    int Limit = 50,
    string? Status = null,
    string? Search = null,
    DateTime? From = null,
    DateTime? To = null);

using FuelFlow.Features.BusinessLeads.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.BusinessLeads.GetAdminBusinessLeads;

public sealed class GetAdminBusinessLeadsQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetAdminBusinessLeadsQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<BusinessLeadListResponse> HandleAsync(
        GetAdminBusinessLeadsQuery query,
        CancellationToken ct = default)
    {
        var leads = _context.BusinessLeads.AsNoTracking();
        var status = query.Status?.Trim();
        var search = query.Search?.Trim();

        if (string.IsNullOrWhiteSpace(status) || status.Equals("active", StringComparison.OrdinalIgnoreCase))
        {
            leads = leads.Where(l => l.Status == BusinessLeadStatus.New
                || l.Status == BusinessLeadStatus.Contacted
                || l.Status == BusinessLeadStatus.Qualified);
        }
        else if (!status.Equals("all", StringComparison.OrdinalIgnoreCase)
                 && Enum.TryParse<BusinessLeadStatus>(status, true, out var parsedStatus))
        {
            leads = leads.Where(l => l.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            leads = leads.Where(l =>
                l.CompanyName.Contains(search)
                || l.ContactName.Contains(search)
                || l.Phone.Contains(search)
                || (l.Email != null && l.Email.Contains(search))
                || (l.Message != null && l.Message.Contains(search)));
        }

        if (query.From.HasValue)
            leads = leads.Where(l => l.CreatedAtUtc >= query.From.Value);
        if (query.To.HasValue)
            leads = leads.Where(l => l.CreatedAtUtc <= query.To.Value);

        var total = await leads.CountAsync(ct);
        var page = await leads
            .OrderByDescending(l => l.CreatedAtUtc)
            .ThenByDescending(l => l.Id)
            .Skip(PageLimits.ClampOffset(query.Offset))
            .Take(PageLimits.ClampPageSize(query.Limit))
            .ToListAsync(ct);
        var items = page.Select(Project).ToList();

        return new BusinessLeadListResponse(total, items);
    }

    internal static AdminBusinessLeadDto Project(BusinessLead lead) => new()
    {
        Id = lead.Id,
        CompanyName = lead.CompanyName,
        ContactName = lead.ContactName,
        Phone = lead.Phone,
        Email = lead.Email,
        FleetSize = lead.FleetSize,
        Message = lead.Message,
        Status = lead.Status.ToString(),
        EmailSentAtUtc = lead.EmailSentAtUtc,
        EmailSendError = lead.EmailSendError,
        CreatedAtUtc = lead.CreatedAtUtc,
        UpdatedAtUtc = lead.UpdatedAtUtc
    };
}

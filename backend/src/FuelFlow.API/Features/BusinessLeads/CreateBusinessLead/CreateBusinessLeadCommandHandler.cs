using FuelFlow.Features.BusinessLeads.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Time;

namespace FuelFlow.Features.BusinessLeads.CreateBusinessLead;

public sealed class CreateBusinessLeadCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly IBusinessLeadMailSender _mailSender;
    private readonly ILogger<CreateBusinessLeadCommandHandler> _logger;

    public CreateBusinessLeadCommandHandler(
        ApplicationDbContext context,
        IBusinessLeadMailSender mailSender,
        ILogger<CreateBusinessLeadCommandHandler> logger)
    {
        _context = context;
        _mailSender = mailSender;
        _logger = logger;
    }

    public async Task<CreateBusinessLeadResponse> HandleAsync(
        CreateBusinessLeadCommand command,
        string? userAgent,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var now = SystemClock.UtcNow;
        var lead = new BusinessLead
        {
            Id = Guid.CreateVersion7(),
            CompanyName = command.CompanyName.Trim(),
            ContactName = command.ContactName.Trim(),
            Phone = command.Phone.Trim(),
            Email = NullIfWhiteSpace(command.Email),
            FleetSize = command.FleetSize,
            Message = NullIfWhiteSpace(command.Message),
            Status = BusinessLeadStatus.New,
            UserAgent = Truncate(userAgent, 300),
            IpAddress = Truncate(ipAddress, 45),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _context.BusinessLeads.Add(lead);
        await _context.SaveChangesAsync(cancellationToken);

        try
        {
            await _mailSender.SendAsync(lead, cancellationToken);
            if (_mailSender.IsConfigured)
                lead.EmailSentAtUtc = SystemClock.UtcNow;
        }
        catch (Exception ex)
        {
            lead.EmailSendError = Truncate(ex.Message, 1000);
            _logger.LogError(
                "Business lead {LeadId} was stored, but sales notification delivery failed ({ErrorType}).",
                lead.Id,
                ex.GetType().Name);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return new CreateBusinessLeadResponse(lead.Id);
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) ? value : value[..Math.Min(value.Length, maxLength)];
}

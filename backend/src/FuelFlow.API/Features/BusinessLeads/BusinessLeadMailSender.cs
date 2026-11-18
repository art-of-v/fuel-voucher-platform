using FuelFlow.Features.BusinessLeads.SharedModels;
using FuelFlow.SharedKernel.Notifications.Email;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Options;

namespace FuelFlow.Features.BusinessLeads;

public interface IBusinessLeadMailSender
{
    bool IsConfigured { get; }
    Task SendAsync(BusinessLead lead, CancellationToken cancellationToken);
}

internal sealed class BusinessLeadMailSender : IBusinessLeadMailSender
{
    private readonly IEmailSender _emailSender;
    private readonly BusinessLeadMailOptions _options;

    public BusinessLeadMailSender(
        IEmailSender emailSender,
        IOptions<BusinessLeadMailOptions> options)
    {
        _emailSender = emailSender;
        _options = options.Value;
    }

    public bool IsConfigured => _emailSender.IsConfigured
        && !string.IsNullOrWhiteSpace(_options.ToEmail);

    public Task SendAsync(BusinessLead lead, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            return Task.CompletedTask;

        var fleet = lead.FleetSize.HasValue ? lead.FleetSize.Value.ToString() : "—";
        var email = lead.Email ?? "—";
        var message = lead.Message ?? "—";
        var text = $"""
            New Fuel Flow business lead

            Company: {lead.CompanyName}
            Contact: {lead.ContactName}
            Phone: {lead.Phone}
            Email: {email}
            Fleet size: {fleet}
            Message: {message}
            Lead ID: {lead.Id}
            """;

        var mail = BrandedEmailLayout.BuildMessage(
            "New Fuel Flow business lead",
            text,
            new BrandedEmailLayout.Content
            {
                Preheader = $"New business enquiry from {lead.CompanyName}",
                Heading = "New business lead",
                Paragraphs =
                [
                    $"Company: {lead.CompanyName}",
                    $"Contact: {lead.ContactName}",
                    $"Phone: {lead.Phone}",
                    $"Email: {email}",
                    $"Fleet size: {fleet}",
                    $"Message: {message}",
                    $"Lead ID: {lead.Id}"
                ],
                Footnote = "Open Fuel Flow to manage this lead."
            });

        return _emailSender.SendAsync(_options.ToEmail, mail, cancellationToken);
    }
}

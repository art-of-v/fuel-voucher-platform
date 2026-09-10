using FuelFlow.Features.Support.SharedModels;
using FuelFlow.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Support.CreateSupportMessage;

public sealed record CreateSupportMessageCommand(string Email, string Message, string? Website);

public sealed record CreateSupportMessageResponse(Guid Id);

public sealed class CreateSupportMessageCommandValidator : AbstractValidator<CreateSupportMessageCommand>
{
    public CreateSupportMessageCommandValidator()
    {
        RuleFor(c => c.Email)
            .NotEmpty().WithMessage("Вкажіть email.")
            .MaximumLength(254)
            .EmailAddress().WithMessage("Email виглядає некоректно.");

        RuleFor(c => c.Message)
            .NotEmpty().WithMessage("Напишіть повідомлення.")
            .MaximumLength(4000);
    }
}

public sealed class CreateSupportMessageCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly ISupportMailSender _mailSender;
    private readonly ILogger<CreateSupportMessageCommandHandler> _logger;

    public CreateSupportMessageCommandHandler(
        ApplicationDbContext context,
        ISupportMailSender mailSender,
        ILogger<CreateSupportMessageCommandHandler> logger)
    {
        _context = context;
        _mailSender = mailSender;
        _logger = logger;
    }

    /// <summary>
    /// Persists the message first, then attempts delivery. Delivery failure is
    /// recorded on the row instead of failing the request: the visitor has done
    /// their part, and a transient SMTP problem must not look like a broken form.
    /// Nothing is re-sent automatically — this is a low-volume human channel.
    /// </summary>
    public async Task<CreateSupportMessageResponse> HandleAsync(
        CreateSupportMessageCommand command,
        string? userAgent,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var message = new SupportMessage
        {
            Id = Guid.NewGuid(),
            Email = command.Email.Trim(),
            Message = command.Message.Trim(),
            UserAgent = Truncate(userAgent, 300),
            IpAddress = ipAddress,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.SupportMessages.Add(message);
        await _context.SaveChangesAsync(cancellationToken);

        try
        {
            await _mailSender.SendAsync(message, cancellationToken);
            // SendAsync throws when delivery fails, and is a silent no-op when
            // SupportMail is unconfigured (IsConfigured=false). Only a genuine
            // success may stamp EmailSentAtUtc - otherwise the row would claim
            // delivery that never happened.
            if (_mailSender.IsConfigured)
                message.EmailSentAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            message.SendError = Truncate(ex.Message, 1000);
            _logger.LogError(ex, "Support message {Id} stored, but email delivery failed.", message.Id);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return new CreateSupportMessageResponse(message.Id);
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) ? value : value[..Math.Min(value.Length, maxLength)];
}

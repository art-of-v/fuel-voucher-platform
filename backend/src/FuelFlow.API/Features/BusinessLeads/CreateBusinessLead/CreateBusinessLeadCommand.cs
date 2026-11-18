using System.Text.RegularExpressions;
using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.BusinessLeads.CreateBusinessLead;

public sealed record CreateBusinessLeadCommand(
    string CompanyName,
    string ContactName,
    string Phone,
    string? Email,
    int? FleetSize,
    string? Message,
    string? Website);

public sealed record CreateBusinessLeadResponse(Guid Id);

public static partial class CreateBusinessLeadCommandValidator
{
    public const int MaxNameLength = 200;
    public const int MaxPhoneLength = 20;
    public const int MaxEmailLength = 254;
    public const int MaxMessageLength = 2000;

    [GeneratedRegex("^\\+[1-9][0-9]{7,14}$", RegexOptions.CultureInvariant)]
    private static partial Regex E164();

    public static Dictionary<string, string[]> Validate(CreateBusinessLeadCommand command)
    {
        var errors = new Dictionary<string, string[]>();
        RequiredBounded(errors, "CompanyName", command.CompanyName, MaxNameLength,
            "Вкажіть назву компанії.", "Назва компанії задовга.");
        RequiredBounded(errors, "ContactName", command.ContactName, MaxNameLength,
            "Вкажіть контактну особу.", "Імʼя контактної особи задовге.");

        var phone = command.Phone?.Trim() ?? string.Empty;
        if (phone.Length == 0)
            errors["Phone"] = ["Вкажіть номер телефону."];
        else if (phone.Length > MaxPhoneLength || !E164().IsMatch(phone))
            errors["Phone"] = ["Вкажіть телефон у міжнародному форматі, наприклад +380970011771."];

        var email = command.Email?.Trim();
        if (!string.IsNullOrEmpty(email))
        {
            if (email.Length > MaxEmailLength)
                errors["Email"] = [$"Email задовгий — максимум {MaxEmailLength} символів."];
            else if (!EmailAddress.IsValid(email))
                errors["Email"] = ["Email виглядає некоректно."];
        }

        if (command.FleetSize is <= 0)
            errors["FleetSize"] = ["Кількість авто має бути більшою за нуль."];

        var message = command.Message?.Trim();
        if (message?.Length > MaxMessageLength)
            errors["Message"] = [$"Повідомлення задовге — максимум {MaxMessageLength} символів."];

        return errors;
    }

    private static void RequiredBounded(
        IDictionary<string, string[]> errors,
        string field,
        string? value,
        int max,
        string required,
        string tooLong)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            errors[field] = [required];
        else if (trimmed.Length > max)
            errors[field] = [$"{tooLong} Максимум {max} символів."];
    }
}

using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Support.CreateSupportMessage;

/// <summary>
/// The rules the public support form is refused with when it breaks them.
///
/// <para>
/// These rules were a FluentValidation validator once. That package is gone (the validation
/// layer was an empty shell: one validator in the whole API, wired to a filter that nothing
/// else used), and the rules live here instead - in the shape the rest of the API validates in,
/// a check the caller can read next to the command it guards.
/// </para>
/// <para>
/// The endpoint's answer is unchanged by the move: a broken field still comes back as a 400
/// with the field named in the <c>errors</c> dictionary, which is what the support page on
/// palne.shop reads.
/// </para>
/// </summary>
public static class CreateSupportMessageCommandValidator
{
    /// <summary>The longest address RFC 5321 allows - 254 characters, local part included.</summary>
    public const int MaxEmailLength = 254;

    /// <summary>
    /// The longest message the form accepts. It is a mail body, not a letter: what arrives is
    /// read by a person, and a megabyte of text is a denial-of-service with a form.
    /// </summary>
    public const int MaxMessageLength = 4000;

    /// <summary>
    /// The errors in <paramref name="command"/>, keyed by the field that broke them. An empty
    /// dictionary means the command is good to store.
    /// </summary>
    public static Dictionary<string, string[]> Validate(CreateSupportMessageCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        // The JSON body can bind a null into a non-nullable property, so the values are read
        // the way they arrive: a missing email is an empty email, not a NullReferenceException.
        var email = command.Email?.Trim() ?? string.Empty;
        var message = command.Message?.Trim() ?? string.Empty;

        var emailErrors = new List<string>();
        if (email.Length == 0)
            emailErrors.Add("Вкажіть email.");
        else if (email.Length > MaxEmailLength)
            emailErrors.Add($"Email задовгий — максимум {MaxEmailLength} символів.");
        else if (!EmailAddress.IsValid(email))
            emailErrors.Add("Email виглядає некоректно.");

        var messageErrors = new List<string>();
        if (message.Length == 0)
            messageErrors.Add("Напишіть повідомлення.");
        else if (message.Length > MaxMessageLength)
            messageErrors.Add($"Повідомлення задовге — максимум {MaxMessageLength} символів.");

        if (emailErrors.Count > 0)
            errors["Email"] = [.. emailErrors];

        if (messageErrors.Count > 0)
            errors["Message"] = [.. messageErrors];

        return errors;
    }
}

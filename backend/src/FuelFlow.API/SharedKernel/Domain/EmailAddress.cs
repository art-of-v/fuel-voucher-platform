using System.Net.Mail;

namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// Whether a string is shaped like an email address.
///
/// <para>
/// One rule, two features: the verified email change (which stages a pending change on a user)
/// and the public support form (which mails a stranger's message to the team) both have to
/// refuse an address that cannot be delivered to. The rule lives here rather than in either
/// feature, because a feature must not reach into another feature's types - that coupling is
/// what the dependency-rule suite forbids.
/// </para>
/// <para>
/// The shape is deliberately loose: it answers "can this be an address?", not "is this a real
/// mailbox?". Nothing here talks to a mail server, and nothing here can tell whether the domain
/// exists - the delivery attempt is what tells that.
/// </para>
/// </summary>
public static class EmailAddress
{
    /// <summary>
    /// Whether <paramref name="value"/> is shaped like an email address.
    /// </summary>
    /// <remarks>
    /// Surrounding whitespace is trimmed first: a form that sends "  someone@example.com  "
    /// means that address, and rejecting it would be a rule about typing rather than about the
    /// address.
    /// </remarks>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();

        try
        {
            var address = new MailAddress(trimmed);

            // MailAddress accepts a display-name form ("Someone <a@b.c>") and hands back the
            // address alone; requiring the round-trip to be identical rejects it, because the
            // platform stores and mails the address the customer typed. The dot requirement is
            // the other half: "a@b" parses, but it is not an address anybody can be reached at.
            return address.Address == trimmed && trimmed.Contains('.');
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

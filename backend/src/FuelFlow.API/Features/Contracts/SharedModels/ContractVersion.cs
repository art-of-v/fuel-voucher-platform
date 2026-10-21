using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Contracts.SharedModels;

/// <summary>
/// One version of a provider's contract text.
/// </summary>
/// <remarks>
/// <para>
/// <b>The text lives once per provider, and a new version is a new row.</b> Every contract of
/// that provider shows the same text, so the text cannot live on the contract - there are
/// thousands of contracts and one text. And a contract that said "version 2" while the
/// provider's text is at version 3 is a question the act and the audit trail must be able to
/// answer, which is why the version is a number and not a pointer to "the current one".
/// </para>
/// <para>
/// <b>The term belongs here, not on the contract.</b> The term a contract carries is the term its
/// text implies: a different version of the terms is a different agreement, and a contract that
/// inherited its term from somewhere else would be able to outlive the text it was signed
/// against.
/// </para>
/// </remarks>
public sealed class ContractVersion
{
    /// <summary>The row's own key.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The provider whose text this is. One text per provider, and the provider is what a
    /// customer signs with - the terms of OKKO are not the terms of WOG.
    /// </summary>
    public Guid BrandId { get; set; }

    /// <summary>
    /// The version number, counting up from 1 within the provider. A new version never
    /// overwrites the old one: what a customer signed is a question the audit trail answers.
    /// </summary>
    public short Version { get; set; }

    /// <summary>
    /// The text itself - what the customer reads before signing. This is the whole point of
    /// the row: before it, the screen's "REVIEW THE TEXT" had nothing behind it.
    /// </summary>
    public string Body { get; set; } = null!;

    /// <summary>
    /// How long a contract made against this version is valid, in days. The term belongs to the
    /// text: a new version of the terms is a different agreement with a different term.
    /// </summary>
    public int TermDays { get; set; }

    /// <summary>When this version was published.</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// Who published it. NULL for a version that was seeded rather than written by somebody.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>The provider this text belongs to.</summary>
    public Brand? Brand { get; set; }
}

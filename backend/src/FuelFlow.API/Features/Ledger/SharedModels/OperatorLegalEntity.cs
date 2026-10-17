namespace FuelFlow.Features.Ledger.SharedModels;

/// <summary>
/// How the operator is taxed. A value, not a derivation: the VAT rate that applies to a document is
/// frozen on that document (D-03), so the regime is recorded rather than recomputed from a lookup
/// that may change.
/// </summary>
public enum OperatorTaxRegime
{
    /// <summary>Платник ПДВ. Confirmed for FuelFlow.</summary>
    Vat,

    /// <summary>Спрощена система оподаткування.</summary>
    Simplified
}

/// <summary>
/// The platform's own legal entity - the one that issues a fiscal receipt, a delivery note or an
/// invoice, and the one whose bank account receives the money.
/// <para>
/// This is not a customer's company and not a supplier. It is *us*, and the reason it is a row is
/// that until it exists every fiscal document has no issuer: no EDRPOU of our own, no tax regime, no
/// bank account and no name to print. It is deliberately seeded inactive - a fiscal document must
/// not become issuable merely because the table now has a row in it.
/// </para>
/// <para>
/// Single-operator is a decision, not an accident, so the database enforces it: at most one row may
/// be active. Re-open only if the business becomes a platform <em>for</em> other fuel operators,
/// which is a different product and not an extension of this one. <c>tenant_id</c> is not added;
/// it would add a key to roughly thirty tables to solve a problem that does not exist.
/// </para>
/// </summary>
public sealed class OperatorLegalEntity
{
    public Guid Id { get; set; }
    public string Edrpou { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? LegalForm { get; set; }
    public string? VatNumber { get; set; }
    public string? Rnkrr { get; set; }
    public OperatorTaxRegime TaxRegime { get; set; }
    public string? RegisteredAddress { get; set; }

    /// <summary>
    /// Whether fiscal documents may name this entity as their issuer. Seeded false; the row becomes
    /// usable only once someone confirms its EDRPOU and VAT number are real.
    /// </summary>
    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<BankAccount> BankAccounts { get; set; } = [];
}

/// <summary>
/// A bank account of the operator's own entity. <see cref="AccountRef"/> holds whatever the bank
/// exposes - an alias or an IBAN - because the two are not interchangeable and the merchant account
/// is the one the acquirer reports against.
/// </summary>
public sealed class BankAccount
{
    public Guid Id { get; set; }
    public Guid OperatorLegalEntityId { get; set; }
    public string BankCode { get; set; } = null!;
    public string AccountRef { get; set; } = null!;
    public string Currency { get; set; } = "UAH";
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;

    public OperatorLegalEntity? OperatorLegalEntity { get; set; }
}
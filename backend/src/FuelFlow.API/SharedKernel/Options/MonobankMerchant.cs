namespace FuelFlow.SharedKernel.Options;

/// <summary>
/// Which Monobank merchant a call belongs to.
/// </summary>
/// <remarks>
/// Production runs two merchants on purpose: a live one that collects real money, and a
/// sandbox one used by QA accounts. They are separate merchant profiles with separate tokens,
/// separate webhook-signing keys and separate settlement accounts - not one profile with a
/// toggle. Which merchant an invoice belongs to is a property of the invoice, so the choice
/// is made once when the invoice is created and then persisted; it is never re-derived later
/// from a policy that might have changed underneath a money movement.
/// </remarks>
public enum MonobankMerchant
{
    /// <summary>The live merchant. Collects real money and settles to the business account.</summary>
    Live = 0,

    /// <summary>The sandbox merchant. Simulates only; no money ever moves.</summary>
    Sandbox = 1
}
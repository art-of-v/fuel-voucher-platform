namespace FuelFlow.Features.Vouchers;

/// <summary>
/// Who caused a recorded fact. Every row in <c>voucher_movements</c> and <c>voucher_events</c> names
/// one, and the database refuses a row that does not.
/// </summary>
/// <remarks>
/// <para>
/// <b>The rule.</b> No action in this system is recorded without an author. That is not a convention a
/// reviewer checks — it is a CHECK constraint, because a convention about audit rows is a convention
/// that erodes, and the first row written without an author is the one nobody notices.
/// </para>
/// <para>
/// <b>Why a kind and not just a nullable id.</b> A <c>uuid</c> with no FK can hold anything, so "always
/// record the actor" against a bare column is unenforceable: the honest-looking options are NULL, or a
/// zero GUID, or a sentinel id that looks like a user and resolves to nobody. Splitting the question
/// into "is this a person or the scheduler" plus "the person's id, required when it is a person"
/// makes all three of those unrepresentable.
/// </para>
/// <para>
/// <b>System actions have an author too.</b> The expiry sweep, the litres-mismatch repair and the
/// retention jobs genuinely have no user, and pretending otherwise with a made-up id would be worse
/// than saying so. <see cref="System"/> names the scheduler, which is an answer an operator can read.
/// </para>
/// </remarks>
public static class VoucherActorKinds
{
    /// <summary>A person: a customer acting for themselves, or an operator acting for the company.</summary>
    public const string User = "user";

    /// <summary>A scheduled or automatic action with no person behind it — an expiry sweep, a repair.</summary>
    public const string System = "system";

    public static readonly IReadOnlyList<string> All = [User, System];
}

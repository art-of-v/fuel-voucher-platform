namespace FuelFlow.Features.Ledger.SharedModels;

/// <summary>
/// A gap-free counter for documents that a human has to cite - journal entries, contracts,
/// fiscal documents. It exists because "number the entries 1, 2, 3" is the kind of statement that
/// looks safe and is not: two concurrent posts, or one retry after a timeout, will hand out the same
/// number, and the unique index on <c>(period_id, entry_no)</c> turns that into an error nobody
/// thought to handle.
/// <para>
/// The counter is advanced inside the caller's transaction, so a rolled-back post does not burn a
/// number. That is why the value is stored rather than computed as <c>MAX(entry_no) + 1</c>.
/// </para>
/// <para>
/// <c>Name</c> is the sequence's identity, not a display label: "journal:&lt;periodId&gt;" means the
/// entry counter for one accounting period, which is exactly the scope the uniqueness constraint
/// uses.
/// </para>
/// </summary>
public sealed class DocumentSequence
{
    public Guid Id { get; set; }

    /// <summary>Identity of the sequence, e.g. <c>journal:3f2b...</c>. Unique.</summary>
    public string Name { get; set; } = null!;

    /// <summary>The last number handed out. Starts at 0, so the first call returns 1.</summary>
    public long LastValue { get; set; }
}
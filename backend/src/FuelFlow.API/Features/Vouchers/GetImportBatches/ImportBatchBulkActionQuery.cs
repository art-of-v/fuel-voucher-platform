namespace FuelFlow.Features.Vouchers.GetImportBatches;

/// <summary>
/// The whole batch, in one action: every voucher of an import, activated or deactivated at once.
/// </summary>
/// <remarks>
/// <para>
/// The admin's two buttons used to act on the ids the tab had loaded, which was the whole batch
/// only because the list was unbounded. The moment the list becomes a page, those ids are a page
/// of the batch, and a button that says "activate all" and activates a page is a button that
/// lies.
/// </para>
/// <para>
/// So the action is the server's: it names the import and the statuses it applies to, and the
/// database decides which rows that is. The cost of saying so is the same as before - the
/// activation gate refuses a batch that is not fully costed - but the answer no longer depends on
/// what the browser happened to be holding.
/// </para>
/// </remarks>
/// <param name="ImportId">The import whose batch this is.</param>
/// <param name="Action">"activate" or "deactivate".</param>
public sealed record ImportBatchBulkActionQuery(
    Guid ImportId,
    string Action,
    Guid? ActingAdminUserId = null,
    string? ActingAdminName = null);

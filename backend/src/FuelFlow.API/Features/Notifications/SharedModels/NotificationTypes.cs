namespace FuelFlow.Features.Notifications.SharedModels;

/// <summary>
/// The kinds of notification a client can act on.
/// </summary>
/// <remarks>
/// <para>
/// A notification used to be a title and a body and nothing else, so the only way to know what a
/// row was about was to read its copy. The copy is not a contract: it changes when the wording
/// changes, it is translated on the client, and a client that has to understand Ukrainian prose to
/// route a notification is a client that breaks when the prose changes.
/// </para>
/// <para>
/// The type is that contract. It is the same vocabulary the push payload uses
/// (<c>data.type</c> in <c>NotificationService</c>), so a push and its in-app twin route the same
/// way - a worker who gets the push lands in the company that issued the fuel, and the row in the
/// bell does the same.
/// </para>
/// <para>
/// The value set is pinned by the database (<c>ck_notification_type</c>), because a typo in a
/// routing key is the worst shape a notification can take: the row reads as ordinary, and the one
/// thing it was for never happens.
/// </para>
/// </remarks>
public static class NotificationTypes
{
    /// <summary>
    /// A notification about nothing in particular: a plain row, read and done. This is what every
    /// row written before the type existed carries.
    /// </summary>
    public const string Generic = "generic";

    /// <summary>
    /// An order was fulfilled. The payload carries the order id.
    /// </summary>
    public const string OrderFulfilled = "order_fulfilled";

    /// <summary>
    /// Fuel was issued to a worker. The payload carries the company id, because a worker may work
    /// for several companies and only the company knows which context the fuel belongs to.
    /// </summary>
    public const string VoucherIssued = "voucher_issued";

    /// <summary>
    /// Every legal type, for the database constraint that pins the set.
    /// </summary>
    public static readonly string[] All = [Generic, OrderFulfilled, VoucherIssued];
}

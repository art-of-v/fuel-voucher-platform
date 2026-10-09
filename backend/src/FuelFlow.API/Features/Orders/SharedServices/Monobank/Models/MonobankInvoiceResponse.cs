namespace FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;

public sealed class MonobankInvoiceResponse
{
    public string InvoiceId { get; set; } = null!;

    /// <summary>
    /// Where to send the customer to pay. With <c>displayType: "iframe"</c> this is the
    /// embeddable variant of Monobank's payment page (<c>pay.monobank.ua/frame/…</c>), which
    /// the mobile app renders inside its own payment screen instead of handing the customer
    /// to a browser.
    /// </summary>
    public string PageUrl { get; set; } = null!;

    /// <summary>
    /// Present only because the create request sets <c>withAppUrl: true</c>: a universal link
    /// that opens the Monobank app directly on the payment confirmation screen. It is what the
    /// "Pay via mono" button uses, and it doubles as the fallback when the in-app page cannot
    /// be rendered - <c>mbnk.app</c> falls back to the web on a device with no Monobank app.
    ///
    /// Null on older API responses, so every consumer must tolerate its absence.
    /// </summary>
    public string? AppUrl { get; set; }
}

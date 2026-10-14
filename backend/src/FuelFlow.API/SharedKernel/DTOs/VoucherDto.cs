namespace FuelFlow.SharedKernel.DTOs;

public sealed class VoucherDto
{
    public Guid Id { get; set; }
    public string Brand { get; set; } = null!;
    public string FuelType { get; set; } = null!;
    public string FuelName { get; set; } = null!;
    public decimal Liters { get; set; }
    public decimal Amount { get; set; }
    public DateOnly ExpirationDate { get; set; }
    public string VoucherNumber { get; set; } = null!;
    public string ExternalId { get; set; } = null!;
    public string QrPayload { get; set; } = null!;
    public string QrCodeData { get; set; } = null!;
    public string Status { get; set; } = null!;
    public Guid? LegalEntityId { get; set; }
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Set when the voucher has been handed to a worker. Its absence is what makes a voucher
    /// "still in the company's pool" for the owner's hub, which reads this list to show what each
    /// order delivered and how much of it is still undistributed.
    /// </summary>
    public Guid? WorkerUserId { get; set; }

    /// <summary>
    /// The voucher's customer-facing timeline (purchase + each renewal, oldest first). Walks across
    /// replace swaps so one tank of fuel reads as a single asset. Empty when the voucher has no
    /// recorded purchase or renewals.
    /// </summary>
    public List<FuelFlow.Features.Vouchers.History.VoucherHistoryEventDto> History { get; set; } = [];

    /// <summary>
    /// The customer's ORIGINAL purchase order for this fuel, resolved across replace swaps. For a
    /// normal or extended voucher this equals its own owning order; for a replacement it is the order
    /// the customer first bought under, not the renewal order that delivered the stock voucher. The
    /// wallet files the live voucher under this order so one tank of fuel stays one asset. Null when
    /// no purchase order backs the lineage (e.g. operator-issued stock).
    /// </summary>
    public Guid? OriginOrderId { get; set; }

    /// <summary>
    /// Transitional alias for <see cref="Brand"/>, serialised under the field's old name.
    /// </summary>
    /// <remarks>
    /// The published 1.0.1 app reads <c>provider</c> off this shape and cannot be made to read
    /// <c>brand</c> - the bundle that reads it is already in the App Store and only a new build can
    /// change it. Dropping the old name before that build is out would leave every current user's
    /// wallet with no brand at all: <c>resolveBrand(undefined)</c> returns null, the cards lose their
    /// network colour, and the filter bar builds its provider list from an empty set.
    ///
    /// So both names ship for one release. Remove this property once 1.1.0 is live and the fleet has
    /// had time to take the OTA - OTA applies on the *second* launch, so "released" is not the same as
    /// "in use". <c>TransitionalBrandAliasTests</c> fails if this is removed while the alias is still
    /// declared and passes once it is gone.
    /// </remarks>
    [System.Text.Json.Serialization.JsonPropertyName("provider")]
    public string Provider => Brand;
}

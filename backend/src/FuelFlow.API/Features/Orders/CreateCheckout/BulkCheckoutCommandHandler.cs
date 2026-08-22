using FuelFlow.API.Features.Orders.CreateCheckout.Models;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.SharedKernel;
using FuelFlow.SharedKernel.Options;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FuelFlow.Features.Orders.CreateCheckout;

public sealed class BulkCheckoutCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly IMonobankClient _monobankClient;
    private readonly MonobankOptions _monobankOptions;
    private readonly ILogger<BulkCheckoutCommandHandler> _logger;

    public BulkCheckoutCommandHandler(
        ApplicationDbContext context,
        IMonobankClient monobankClient,
        IOptions<MonobankOptions> monobankOptions,
        ILogger<BulkCheckoutCommandHandler> logger)
    {
        _context = context;
        _monobankClient = monobankClient;
        _monobankOptions = monobankOptions.Value;
        _logger = logger;
    }

    public async Task<BulkCheckoutResponse> HandleAsync(
        BulkCheckoutCommand command,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Creating bulk checkout for user {UserId} with {ItemCount} items",
            command.UserId, command.Items.Count);

        if (command.UserId == null || command.UserId == Guid.Empty)
            throw new ArgumentException("UserId is required", nameof(command));

        if (command.Items.Count == 0)
            throw new ArgumentException("At least one item is required", nameof(command));

        // Money integrity: a negative or zero quantity would subtract from (or not contribute to)
        // the invoice total while fulfilment still assigns vouchers for the remaining positive
        // lines. Enforced here as well as in the controller so no other caller can skip it.
        foreach (var item in command.Items)
        {
            if (item.Liters <= 0)
                throw new ArgumentException("Liters must be greater than 0 for every item", nameof(command));

            if (item.Quantity <= 0)
                throw new ArgumentException("Quantity must be greater than 0 for every item", nameof(command));
        }

        if (command.LegalEntityId.HasValue)
        {
            var ownsLegalEntity = await _context.LegalEntities
                .AsNoTracking()
                .AnyAsync(x => x.Id == command.LegalEntityId.Value && x.UserId == command.UserId.Value, cancellationToken);

            if (!ownsLegalEntity)
            {
                throw new ArgumentException("Provided LegalEntityId does not belong to the user.", nameof(command));
            }
        }

        var stationIds = command.Items.Select(i => i.StationId!).Distinct().ToList();
        var fuelTypeIds = command.Items.Select(i => i.FuelTypeId).ToList();

        var fuelTypes = await _context.FuelTypes
            .Where(f => stationIds.Contains(f.StationId) && fuelTypeIds.Contains(f.Id))
            .ToListAsync(cancellationToken);

        var packages = await _context.FuelPackages
            .Where(p => stationIds.Contains(p.StationId) && fuelTypeIds.Contains(p.FuelTypeId))
            .ToListAsync(cancellationToken);

        var itemPricing = new List<(CheckoutItem Item, int UnitPrice, int LineTotal)>();
        var totalPrice = 0;

        foreach (var item in command.Items)
        {
            var fuelType = fuelTypes.FirstOrDefault(f =>
                f.Id == item.FuelTypeId && f.StationId == item.StationId);

            if (fuelType == null)
                throw new ArgumentException(
                    $"Invalid fuel type ID: {item.FuelTypeId} for station {item.StationId}");

            var package = packages.FirstOrDefault(p =>
                p.StationId == item.StationId &&
                p.FuelTypeId == item.FuelTypeId &&
                p.Liters == item.Liters);

            if (package == null)
                throw new ArgumentException(
                    $"No pricing found for fuel type {item.FuelTypeId} at station {item.StationId} for {item.Liters}L");

            var unitPrice = ServerPricing.PackagePrice(package, item.Liters);

            // checked: silent int wraparound here would decouple the amount we invoice from
            // the vouchers we hand out. Overflow must fail the request, not wrap to a small total.
            int lineTotal;
            try
            {
                lineTotal = checked(unitPrice * item.Quantity);
                totalPrice = checked(totalPrice + lineTotal);
            }
            catch (OverflowException)
            {
                throw new ArgumentException("Order total is too large", nameof(command));
            }

            if (item.Price != lineTotal)
            {
                _logger.LogWarning(
                    "Client price {ClientPrice} does not match server price {ServerPrice} for user {UserId}, fuel {FuelTypeId}; using server price",
                    item.Price, lineTotal, command.UserId, item.FuelTypeId);
            }

            itemPricing.Add((item, unitPrice, lineTotal));
        }

        MonobankInvoiceResponse invoiceResponse;
        try
        {
            invoiceResponse = await _monobankClient.CreateInvoiceAsync(
                new MonobankInvoiceRequest
                {
                    // Monobank is the one place amounts must be kopecks.
                    Amount = Money.ToKopecks(totalPrice),
                    MerchantPaymentInfo = $"FuelFlow Bundle",
                    RedirectUrl = _monobankOptions.RedirectUrl,
                    WebhookUrl = _monobankOptions.WebhookUrl
                }, cancellationToken);

            _logger.LogInformation(
                "Monobank invoice created for bundle: {InvoiceId}",
                invoiceResponse.InvoiceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Monobank invoice for bundle");
            throw;
        }

        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = command.UserId!.Value,
            LegalEntityId = command.LegalEntityId,
            Price = totalPrice,
            Status = OrderStatus.PendingPayment,
            MonobankInvoiceId = invoiceResponse.InvoiceId,
            MonobankPaymentUrl = invoiceResponse.PageUrl,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        foreach (var (item, unitPrice, lineTotal) in itemPricing)
        {
            order.LineItems.Add(new OrderLineItem
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                Provider = item.StationId!,
                FuelTypeId = item.FuelTypeId,
                Liters = item.Liters,
                Quantity = item.Quantity,
                UnitPrice = unitPrice,
                LineTotal = lineTotal
            });
        }

        _context.Orders.Add(order);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Bulk checkout created with {LineItemCount} line items, order {OrderId}, invoice {InvoiceId}",
            order.LineItems.Count, order.Id, invoiceResponse.InvoiceId);

        return new BulkCheckoutResponse
        {
            OrderIds = [order.Id],
            MonobankInvoiceId = invoiceResponse.InvoiceId,
            PaymentUrl = invoiceResponse.PageUrl
        };
    }
}

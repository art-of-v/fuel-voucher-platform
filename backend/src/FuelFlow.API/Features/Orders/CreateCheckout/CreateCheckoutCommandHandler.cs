using FuelFlow.API.Features.Orders.CreateCheckout.Models;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FuelFlow.Features.Orders.CreateCheckout;

public sealed class CreateCheckoutCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly IMonobankClient _monobankClient;
    private readonly MonobankOptions _monobankOptions;
    private readonly ILogger<CreateCheckoutCommandHandler> _logger;

    public CreateCheckoutCommandHandler(
        ApplicationDbContext context,
        IMonobankClient monobankClient,
        IOptions<MonobankOptions> monobankOptions,
        ILogger<CreateCheckoutCommandHandler> logger)
    {
        _context = context;
        _monobankClient = monobankClient;
        _monobankOptions = monobankOptions.Value;
        _logger = logger;
    }

    public async Task<CreateCheckoutResponse> HandleAsync(
        CreateCheckoutCommand command,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating checkout for user {UserId}", command.UserId);

        if (string.IsNullOrWhiteSpace(command.UserId))
        {
            throw new ArgumentException("UserId is required", nameof(command));
        }

        if (string.IsNullOrWhiteSpace(command.StationId))
        {
            throw new ArgumentException("StationId is required", nameof(command));
        }

        var fuelTypeEntity = await _context.FuelTypes
            .FirstOrDefaultAsync(f => f.Id == command.FuelTypeId && f.StationId == command.StationId, cancellationToken);

        if (fuelTypeEntity == null)
        {
            throw new ArgumentException($"Invalid fuel type ID: {command.FuelTypeId} for station {command.StationId}");
        }

        var timeWindow = DateTime.UtcNow.ToString("yyyyMMddHHmm").Substring(0, 11);
        var roundedMinute = (DateTime.UtcNow.Minute / 5) * 5;
        var idempotencyKey = $"{command.UserId}:{command.StationId}:{command.FuelTypeId}:{command.Liters}:{command.Quantity}:{DateTime.UtcNow:yyyyMMddHH}{roundedMinute:D2}";

        var existingOrder = await _context.Orders
            .FirstOrDefaultAsync(o => o.IdempotencyKey == idempotencyKey
                                      && o.CreatedAtUtc > DateTime.UtcNow.AddHours(-1), cancellationToken);

        if (existingOrder != null && !string.IsNullOrEmpty(existingOrder.MonobankPaymentUrl))
        {
            _logger.LogWarning("Duplicate order creation attempt detected for key {IdempotencyKey}", idempotencyKey);
            return new CreateCheckoutResponse
            {
                OrderId = existingOrder.Id,
                Status = existingOrder.Status.ToString(),
                MonobankInvoiceId = existingOrder.MonobankInvoiceId,
                PaymentUrl = existingOrder.MonobankPaymentUrl
            };
        }

        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = command.UserId,
            ProductType = $"{command.StationId} {fuelTypeEntity.Name} {command.Liters}L",
            Provider = command.StationId,
            FuelTypeId = command.FuelTypeId,
            Liters = command.Liters,
            Quantity = command.Quantity,
            Price = command.Price,
            Status = OrderStatus.PendingPayment,
            IdempotencyKey = idempotencyKey,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.Orders.Add(order);

        try
        {
            var invoiceRequest = new MonobankInvoiceRequest
            {
                Amount = command.Price * 100,
                MerchantPaymentInfo = $"FuelFlow Order {order.Id}",
                RedirectUrl = _monobankOptions.RedirectUrl,
                WebhookUrl = _monobankOptions.WebhookUrl
            };

            var invoiceResponse = await _monobankClient.CreateInvoiceAsync(invoiceRequest, cancellationToken);

            order.MonobankInvoiceId = invoiceResponse.InvoiceId;
            order.MonobankPaymentUrl = invoiceResponse.PageUrl;

            _logger.LogInformation(
                "Monobank invoice created for order {OrderId}: {InvoiceId}, payment URL: {PaymentUrl}",
                order.Id,
                invoiceResponse.InvoiceId,
                invoiceResponse.PageUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Monobank invoice for order {OrderId}", order.Id);
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Order {OrderId} created successfully", order.Id);

        return new CreateCheckoutResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            MonobankInvoiceId = order.MonobankInvoiceId,
            PaymentUrl = order.MonobankPaymentUrl
        };
    }
}


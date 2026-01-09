using Stripe;
using Stripe.Checkout;

namespace FuelFlow.Api.Infrastructure.Services;

public interface IStripeService
{
    Task<Session> CreateCheckoutSessionAsync(string packageId, string stationName, string fuelName, int liters, int price);
    Event ConstructWebhookEvent(string json, string signature, string secret);
    string GetPublishableKey();
}

public class StripeService : IStripeService
{
    private readonly string? _secretKey;
    private readonly string? _publishableKey;
    private readonly string? _webhookSecret;
    private readonly bool _isConfigured;

    public StripeService(IConfiguration configuration)
    {
        _secretKey = configuration["Stripe:SecretKey"] 
            ?? Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY");
        
        _publishableKey = configuration["Stripe:PublishableKey"] 
            ?? Environment.GetEnvironmentVariable("STRIPE_PUBLISHABLE_KEY");
        
        _webhookSecret = configuration["Stripe:WebhookSecret"] 
            ?? Environment.GetEnvironmentVariable("STRIPE_WEBHOOK_SECRET");

        _isConfigured = !string.IsNullOrEmpty(_secretKey);

        if (_isConfigured)
        {
            StripeConfiguration.ApiKey = _secretKey;
        }
    }

    public async Task<Session> CreateCheckoutSessionAsync(string packageId, string stationName, string fuelName, int liters, int price)
    {
        if (!_isConfigured)
            throw new InvalidOperationException("Stripe not configured");

        var options = new SessionCreateOptions
        {
            PaymentMethodTypes = new List<string> { "card" },
            LineItems = new List<SessionLineItemOptions>
            {
                new SessionLineItemOptions
                {
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = "uah",
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = $"{stationName} - {fuelName}",
                            Description = $"{liters}L of {fuelName}"
                        },
                        UnitAmount = price * 100
                    },
                    Quantity = 1
                }
            },
            Mode = "payment",
            SuccessUrl = $"{Environment.GetEnvironmentVariable("FRONTEND_URL")}/success?session_id={{CHECKOUT_SESSION_ID}}",
            CancelUrl = $"{Environment.GetEnvironmentVariable("FRONTEND_URL")}/checkout",
            Metadata = new Dictionary<string, string>
            {
                { "packageId", packageId },
                { "liters", liters.ToString() }
            }
        };

        var service = new SessionService();
        return await service.CreateAsync(options);
    }

    public Event ConstructWebhookEvent(string json, string signature, string secret)
    {
        return EventUtility.ConstructEvent(json, signature, secret);
    }

    public string GetPublishableKey()
    {
        return _publishableKey ?? "";
    }
}

using System.Text.Json;
using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.API.BackgroundJobs.Models;
using Xunit;

namespace FuelFlow.UnitTests.BackgroundJobs;

/// <summary>
/// The two halves of the ORDER_CREATED contract: what the producer writes and what the consumer reads.
/// </summary>
/// <remarks>
/// The event is written from an anonymous object in <c>ProcessMonobankWebhookCommandHandler</c>, so
/// its keys are camelCase, while <see cref="OrderCreatedPayload"/> is PascalCase. Read with
/// <see cref="JsonSerializer"/>'s default (case-sensitive) options, every property stayed at its
/// default: the handler was handed <see cref="Guid.Empty"/>, searched for that order, found nothing,
/// and marked the event processed. Fulfilment then relied entirely on the open-order sweep.
/// </remarks>
public sealed class OrderCreatedPayloadTests
{
    [Fact]
    public void The_payload_the_outbox_writes_names_its_order()
    {
        var orderId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // Exactly the shape ProcessMonobankWebhookCommandHandler.WebhookProcessing.cs writes.
        var json = JsonSerializer.Serialize(new
        {
            orderId,
            userId,
            brand = "okko",
            fuelType = Guid.NewGuid(),
            liters = 10m,
            quantity = 1
        });

        var payload = JsonSerializer.Deserialize<OrderCreatedPayload>(json, OutboxPayloadJson.Options);

        payload.Should().NotBeNull();
        payload!.OrderId.Should().Be(orderId);
        payload.UserId.Should().Be(userId.ToString());
    }

    [Fact]
    public void A_payload_without_an_order_id_is_missing_rather_than_the_empty_id()
    {
        var payload = JsonSerializer.Deserialize<OrderCreatedPayload>("{}", OutboxPayloadJson.Options);

        // The handler's guard is `payload?.OrderId is not { } orderId`. With a non-nullable Guid this
        // was always false, so the "invalid payload" branch could never run.
        payload!.OrderId.Should().BeNull();
    }
}

using System.Text.Json;

namespace FuelFlow.API.BackgroundJobs;

/// <summary>
/// The options every outbox payload is read with.
/// </summary>
/// <remarks>
/// Producers serialise payloads from anonymous objects, so the keys are camelCase (<c>orderId</c>),
/// while the payload records are PascalCase - and <see cref="JsonSerializer"/>'s default options are
/// case-sensitive. A consumer that reads with the default therefore binds nothing: <c>ORDER_CREATED</c>
/// was handled as "order 00000000-0000-0000-0000-000000000000 not found" and marked processed on
/// every paid order, so the event path never fulfilled anything and only the open-order sweep kept
/// customers supplied. <see cref="JsonSerializerDefaults.Web"/> makes the read case-insensitive.
/// </remarks>
internal static class OutboxPayloadJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

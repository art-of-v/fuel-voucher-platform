namespace FuelFlow.SharedKernel.Options;

/// <summary>
/// Everything needed to call one Monobank merchant: the token its API authenticates with and the
/// public key that verifies its webhooks.
/// </summary>
/// <remarks>
/// The base URL is separate from the merchant because both merchants live on the same host - the
/// sandbox is a distinct merchant profile, not a distinct endpoint.
/// </remarks>
public sealed record MonobankCredentials(string Token, string BaseUrl, string PublicKey);
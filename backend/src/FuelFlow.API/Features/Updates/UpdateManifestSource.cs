using System.Net;
using System.Net.Http.Json;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Options;

namespace FuelFlow.Features.Updates;

/// <summary>
/// Fetches the pre-signed <see cref="UpdateDescriptor"/> for a platform + runtime version from the
/// public R2 bucket. Deliberately a plain typed <see cref="HttpClient"/> over public HTTPS rather than
/// an S3 SDK: the bucket is public-read and the update's integrity comes from the manifest signature
/// the client verifies, not from the transport or any bucket credential, so no secret lives on this
/// path.
/// </summary>
public sealed class UpdateManifestSource
{
    private readonly HttpClient _httpClient;
    private readonly UpdatesOptions _options;

    public UpdateManifestSource(HttpClient httpClient, IOptions<UpdatesOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    /// <summary>True once a public R2 base URL is configured; false is the fail-safe "no OTA yet".</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.PublicBaseUrl);

    /// <summary>
    /// Returns the descriptor published for this platform/runtime, or <c>null</c> when none exists
    /// (the upstream object is absent — a normal "no update available"). Throws on any other upstream
    /// failure so the controller can answer 502 rather than masking a broken CDN as "no update".
    /// </summary>
    public async Task<UpdateDescriptor?> GetDescriptorAsync(
        string platform,
        string runtimeVersion,
        CancellationToken cancellationToken)
    {
        // Content-addressed layout the Mac publish script mirrors: {platform}/{runtimeVersion}/update.json.
        // Both segments are URL-escaped so a runtime version expressed as a fingerprint hash stays safe
        // in the path.
        var key = $"{Uri.EscapeDataString(platform)}/{Uri.EscapeDataString(runtimeVersion)}/update.json";
        var url = $"{_options.PublicBaseUrl.TrimEnd('/')}/{key}";

        using var response = await _httpClient.GetAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var descriptor = await response.Content.ReadFromJsonAsync<UpdateDescriptor>(cancellationToken);
        if (descriptor is null
            || string.IsNullOrEmpty(descriptor.ManifestBase64)
            || string.IsNullOrEmpty(descriptor.Signature))
        {
            throw new InvalidOperationException(
                $"Update descriptor at '{key}' is missing manifestBase64 or signature.");
        }

        return descriptor;
    }
}

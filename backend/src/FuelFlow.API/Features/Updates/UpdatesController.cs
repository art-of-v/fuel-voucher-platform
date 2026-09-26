using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Updates;

// Self-hosted Expo Updates endpoint (expo-updates protocol v1). It re-enables signed over-the-air JS
// updates without the EAS-hosted publish plan: the Mac publish script (mobile/scripts/publish-ota.mjs)
// signs every manifest with a private key that never leaves that machine and uploads the pre-signed
// bytes to public Cloudflare R2; this endpoint only fetches and relays them. A stolen R2 token or a
// compromised API therefore cannot forge an update the app will install — expo-updates verifies each
// manifest's signature against the certificate baked into the build and keeps its last-good bundle on
// any mismatch. See planning issue #42.
//
// [AllowAnonymous] is explicit and mandatory: AuthSetup installs a RequireAuthenticatedUser fallback
// policy, and an update client carries no bearer token. The route is pinned in
// AnonymousEndpointSurfaceTests so this public-surface decision stays reviewed rather than incidental.
[ApiController]
[Route("api/updates")]
[AllowAnonymous]
public sealed class UpdatesController : ControllerBase
{
    private readonly UpdateManifestSource _source;
    private readonly ILogger<UpdatesController> _logger;

    public UpdatesController(UpdateManifestSource source, ILogger<UpdatesController> logger)
    {
        _source = source;
        _logger = logger;
    }

    /// <summary>
    /// expo-updates manifest endpoint. The client sends its platform and runtime version as headers
    /// and expects either a signed manifest (200, multipart/mixed) or "nothing newer" (204).
    /// </summary>
    [HttpGet("manifest")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> GetManifest(CancellationToken cancellationToken)
    {
        var platform = Request.Headers["expo-platform"].ToString();
        var runtimeVersion = Request.Headers["expo-runtime-version"].ToString();

        if (string.IsNullOrWhiteSpace(platform) || string.IsNullOrWhiteSpace(runtimeVersion))
            return BadRequest("expo-platform and expo-runtime-version headers are required.");

        // No bucket configured yet (the fail-safe default before R2 exists or the first publish has
        // run): answer a valid "no update available" so the installed bundle keeps running.
        if (!_source.IsConfigured)
            return NoUpdate();

        byte[] manifestBytes;
        string signature;
        try
        {
            var descriptor = await _source.GetDescriptorAsync(platform, runtimeVersion, cancellationToken);
            if (descriptor is null)
                return NoUpdate();

            // Decoded here, inside the try, so a malformed base64 payload from R2 degrades to a 502
            // rather than an unhandled 500.
            manifestBytes = Convert.FromBase64String(descriptor.ManifestBase64);
            signature = descriptor.Signature;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // genuine client disconnect / shutdown — let the framework handle it
        }
        catch (Exception ex)
        {
            // Unreachable CDN, an upstream 5xx, or a malformed descriptor. Fail loud to monitoring; the
            // client still falls back to its last-good bundle, so this degrades to "no update", never a
            // bad one.
            _logger.LogError(ex, "Serving update manifest failed for {Platform}/{RuntimeVersion}",
                platform, runtimeVersion);
            return StatusCode(StatusCodes.Status502BadGateway);
        }

        return BuildManifestResponse(manifestBytes, signature);
    }

    // Protocol v1 signals "no newer update" with a 204 and no body; the client stays on its current
    // bundle. The protocol headers are set anyway so a strict client accepts the response.
    private NoContentResult NoUpdate()
    {
        SetProtocolHeaders();
        return NoContent();
    }

    private FileContentResult BuildManifestResponse(byte[] manifestBytes, string signature)
    {
        // A multipart/mixed body with a single "manifest" part. The part body is the exact bytes the
        // Mac signed — re-serialising the JSON here would change them and invalidate the signature, so
        // they are copied through verbatim. The signature rides on the part's expo-signature header,
        // per the protocol.
        var boundary = "ff-" + Guid.NewGuid().ToString("N");
        var head = Encoding.UTF8.GetBytes(
            $"--{boundary}\r\n" +
            "content-disposition: form-data; name=\"manifest\"\r\n" +
            "content-type: application/json; charset=utf-8\r\n" +
            $"expo-signature: {signature}\r\n" +
            "\r\n");
        var tail = Encoding.UTF8.GetBytes($"\r\n--{boundary}--\r\n");

        var body = new byte[head.Length + manifestBytes.Length + tail.Length];
        Buffer.BlockCopy(head, 0, body, 0, head.Length);
        Buffer.BlockCopy(manifestBytes, 0, body, head.Length, manifestBytes.Length);
        Buffer.BlockCopy(tail, 0, body, head.Length + manifestBytes.Length, tail.Length);

        SetProtocolHeaders();
        return File(body, $"multipart/mixed; boundary=\"{boundary}\"");
    }

    private void SetProtocolHeaders()
    {
        Response.Headers["expo-protocol-version"] = "1";
        Response.Headers["expo-sfv-version"] = "0";
        // Per-device response (it varies by the expo-platform / expo-runtime-version request headers),
        // so it must never be stored by a shared cache.
        Response.Headers.CacheControl = "private, max-age=0";
    }
}

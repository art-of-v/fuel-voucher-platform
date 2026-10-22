using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.PlayIntegrity.v1;
using Google.Apis.PlayIntegrity.v1.Data;
using Google.Apis.Services;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Time;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FuelFlow.SharedKernel.Security.Attestation;

/// <summary>
/// Decodes Play Integrity tokens through Google's API and applies the verdict policy.
/// </summary>
/// <remarks>
/// The token itself is opaque to us: decoding it locally would mean trusting a key we do not have
/// and re-implementing an encryption scheme. The API call is the verifier. The service account JSON
/// is supplied through the environment and is never committed.
/// </remarks>
public sealed class PlayIntegrityVerifier : IPlayIntegrityVerifier
{
    private const string PlayIntegrityScope = "https://www.googleapis.com/auth/playintegrity";

    private readonly IOptionsMonitor<AttestationOptions> _options;
    private readonly ILogger<PlayIntegrityVerifier> _logger;
    private readonly object _gate = new();
    private PlayIntegrityService? _service;
    private string? _serviceCredentials;

    public PlayIntegrityVerifier(
        IOptionsMonitor<AttestationOptions> options,
        ILogger<PlayIntegrityVerifier> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<PlayIntegrityResult> VerifyAsync(
        string integrityToken,
        string expectedRequestHash,
        CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;

        if (string.IsNullOrWhiteSpace(options.GoogleCredentialsJson))
        {
            return PlayIntegrityResult.Invalid(
                "GOOGLE_NOT_CONFIGURED",
                "Play Integrity verification is not configured on the server.");
        }

        DecodeIntegrityTokenResponse response;

        try
        {
            response = await GetService(options)
                .V1
                .DecodeIntegrityToken(
                    new DecodeIntegrityTokenRequest { IntegrityToken = integrityToken },
                    options.GooglePackageName)
                .ExecuteAsync(cancellationToken);
        }
        catch (GoogleApiException exception)
        {
            var status = (int)exception.HttpStatusCode;

            _logger.LogWarning(
                exception,
                "Play Integrity decode failed with HTTP {Status} for package {Package}",
                status,
                options.GooglePackageName);

            if (status is 401 or 403 or 429 or >= 500)
            {
                throw new AttestationVerificationException(
                    "ATTESTATION_UNAVAILABLE",
                    "The Play Integrity verifier is not available.",
                    exception);
            }

            return PlayIntegrityResult.Invalid(
                "GOOGLE_TOKEN_INVALID",
                "The Play Integrity token could not be decoded.");
        }
        catch (HttpRequestException exception)
        {
            throw new AttestationVerificationException(
                "ATTESTATION_UNAVAILABLE",
                "The Play Integrity verifier could not be reached.",
                exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AttestationVerificationException(
                "ATTESTATION_UNAVAILABLE",
                "The Play Integrity verifier timed out.",
                exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new AttestationVerificationException(
                "ATTESTATION_UNAVAILABLE",
                "The Play Integrity verifier could not be initialised.",
                exception);
        }

        var payload = response.TokenPayloadExternal;

        if (payload is null)
        {
            return PlayIntegrityResult.Invalid(
                "GOOGLE_TOKEN_INVALID",
                "The Play Integrity response contained no payload.");
        }

        var app = payload.AppIntegrity;
        var device = payload.DeviceIntegrity;
        var request = payload.RequestDetails;
        var testing = payload.TestingDetails;

        var appVerdict = app?.AppRecognitionVerdict;
        var deviceVerdicts = device?.DeviceRecognitionVerdict ?? Array.Empty<string>();
        var deviceVerdict = string.Join(",", deviceVerdicts);
        var isTesting = testing?.IsTestingResponse;

        if (!string.Equals(appVerdict, "PLAY_RECOGNIZED", StringComparison.Ordinal))
        {
            return PlayIntegrityResult.Invalid(
                "GOOGLE_APP_NOT_RECOGNIZED",
                "The app was not recognised by Google Play.",
                appVerdict,
                deviceVerdict,
                isTesting);
        }

        if (!string.Equals(app?.PackageName, options.GooglePackageName, StringComparison.Ordinal))
        {
            return PlayIntegrityResult.Invalid(
                "GOOGLE_PACKAGE_MISMATCH",
                "The Play Integrity token is for a different package.",
                appVerdict,
                deviceVerdict,
                isTesting);
        }

        if (options.GoogleAllowedCertificateDigests.Count > 0)
        {
            var digests = app?.CertificateSha256Digest ?? Array.Empty<string>();

            if (!digests.Any(digest => options.GoogleAllowedCertificateDigests.Contains(
                    digest,
                    StringComparer.Ordinal)))
            {
                return PlayIntegrityResult.Invalid(
                    "GOOGLE_CERTIFICATE_MISMATCH",
                    "The app signing certificate is not the expected one.",
                    appVerdict,
                    deviceVerdict,
                    isTesting);
            }
        }

        if (!deviceVerdicts.Any(verdict => options.GoogleAllowedDeviceVerdicts.Contains(
                verdict,
                StringComparer.Ordinal)))
        {
            return PlayIntegrityResult.Invalid(
                "GOOGLE_DEVICE_VERDICT_MISSING",
                "The device did not meet the integrity requirement.",
                appVerdict,
                deviceVerdict,
                isTesting);
        }

        if (request is null
            || string.IsNullOrEmpty(request.RequestHash)
            || !AttestationEncoding.FixedTimeEquals(request.RequestHash, expectedRequestHash))
        {
            return PlayIntegrityResult.Invalid(
                "GOOGLE_HASH_MISMATCH",
                "The Play Integrity token is not bound to this request.",
                appVerdict,
                deviceVerdict,
                isTesting);
        }

        if (!string.Equals(request.RequestPackageName, options.GooglePackageName, StringComparison.Ordinal))
        {
            return PlayIntegrityResult.Invalid(
                "GOOGLE_PACKAGE_MISMATCH",
                "The Play Integrity request names a different package.",
                appVerdict,
                deviceVerdict,
                isTesting);
        }

        if (request.TimestampMillis is null)
        {
            return PlayIntegrityResult.Invalid(
                "GOOGLE_TIMESTAMP_MISSING",
                "The Play Integrity token carries no request timestamp.",
                appVerdict,
                deviceVerdict,
                isTesting);
        }

        var now = SystemClock.Now.ToUnixTimeMilliseconds();
        var age = Math.Abs((double)now - request.TimestampMillis.Value);

        if (age > options.GoogleClockSkewSeconds * 1000d)
        {
            return PlayIntegrityResult.Invalid(
                "GOOGLE_TIMESTAMP_INVALID",
                "The Play Integrity token is outside the accepted time window.",
                appVerdict,
                deviceVerdict,
                isTesting);
        }

        if (isTesting == true && !options.GoogleAllowTestingResponses)
        {
            return PlayIntegrityResult.Invalid(
                "GOOGLE_TEST_RESPONSE",
                "A Play Integrity test response is not accepted here.",
                appVerdict,
                deviceVerdict,
                isTesting);
        }

        return PlayIntegrityResult.Valid(appVerdict, deviceVerdict, isTesting);
    }

    private PlayIntegrityService GetService(AttestationOptions options)
    {
        lock (_gate)
        {
            if (_service is not null
                && string.Equals(_serviceCredentials, options.GoogleCredentialsJson, StringComparison.Ordinal))
            {
                return _service;
            }

            var credential = CredentialFactory
                .FromJson<ServiceAccountCredential>(options.GoogleCredentialsJson!)
                .ToGoogleCredential()
                .CreateScoped(PlayIntegrityScope);

            _service = new PlayIntegrityService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "FuelFlow",
            });
            _serviceCredentials = options.GoogleCredentialsJson;

            return _service;
        }
    }
}

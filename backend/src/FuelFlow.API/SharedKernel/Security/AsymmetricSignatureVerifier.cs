using System.Security.Cryptography;
using System.Text;

namespace FuelFlow.SharedKernel.Security;

/// <summary>
/// The signature schemes a caller can pin a key to.
/// </summary>
/// <remarks>
/// The scheme cannot be inferred from the key: an RSA public key is the same bytes whether its
/// owner signs with PKCS#1 v1.5 or PSS, and an EC key is the same whether its owner emits DER or
/// IEEE-P1363 signatures. Trying every scheme is how a verifier accepts a signature it should not
/// have to understand, so a device enrolled by this platform records the one scheme it uses and
/// the verifier is only allowed to run that one.
/// </remarks>
public enum AsymmetricSignatureAlgorithm
{
    RsaPkcs1Sha256,
    RsaPssSha256,
    EcdsaSha256Der,
    EcdsaSha256P1363
}

/// <summary>One scheme's outcome for one payload, reported for diagnostics and for pinning.</summary>
public sealed record SignatureAttempt(
    AsymmetricSignatureAlgorithm Algorithm,
    bool IsValid,
    string? Error);

/// <summary>
/// The full outcome of a verification: which scheme matched, which were tried, and why nothing
/// matched. The boolean callers never need this, but the device paths do: they pin the scheme that
/// worked on a legacy row and they report the attempts in the <c>verify-raw</c> diagnostic.
/// </summary>
public sealed class SignatureVerificationResult
{
    public SignatureVerificationResult(
        IReadOnlyList<SignatureAttempt> attempts,
        string? keyType,
        string? failure)
    {
        Attempts = attempts;
        KeyType = keyType;
        Failure = failure;
    }

    public IReadOnlyList<SignatureAttempt> Attempts { get; }

    /// <summary><c>RSA</c>, <c>ECDSA</c>, or null when the key never imported as either.</summary>
    public string? KeyType { get; }

    /// <summary>
    /// Why the check could not run at all (<c>invalid-base64</c>, <c>unreadable-key</c>), or null
    /// when at least one scheme was attempted.
    /// </summary>
    public string? Failure { get; }

    public AsymmetricSignatureAlgorithm? MatchedAlgorithm
    {
        get
        {
            foreach (var attempt in Attempts)
            {
                if (attempt.IsValid)
                {
                    return attempt.Algorithm;
                }
            }

            return null;
        }
    }

    public bool IsValid => MatchedAlgorithm is not null;
}

/// <summary>
/// Verifies asymmetric (RSA/ECDSA) signatures over an arbitrary UTF-8 payload.
/// Accepts a public key in three forms:
///   - PEM ("-----BEGIN PUBLIC KEY----- ..."),
///   - base64-encoded PEM (exactly what Monobank's GET /api/merchant/pubkey returns),
///   - raw base64 subject-public-key-info (DER) bytes.
/// Supports RSA SHA-256 PKCS#1 and PSS, and ECDSA SHA-256 (DER and IEEE-P1363 encodings),
/// including the secp256k1 curve used by Monobank acquiring webhooks (works on Linux;
/// Windows CNG does not implement secp256k1).
/// </summary>
/// <remarks>
/// Which exception the key import raises depends on the platform, and the code has to survive both:
/// on Linux an EC key offered to <c>RSA.ImportFromPem</c> throws <see cref="ArgumentException"/>,
/// while the same call on Windows throws <see cref="CryptographicException"/>. Catching only one of
/// them made ECDSA-signed webhooks work on a developer machine and fail in production, where every
/// Monobank callback answered 500.
/// <para>
/// This class is the only place the platform implements that checking. <see cref="Verify"/> is the
/// lenient shape webhooks need; <see cref="VerifyWith"/> is the pinned shape the device paths use,
/// so a second implementation cannot drift from this one.
/// </para>
/// </remarks>
public interface IAsymmetricSignatureVerifier
{
    bool Verify(string payload, string signatureBase64, string publicKey);

    /// <summary>
    /// Verifies <paramref name="payload"/> against only the listed schemes. The order of
    /// <paramref name="allowedAlgorithms"/> is the order attempts are reported in.
    /// </summary>
    SignatureVerificationResult VerifyWith(
        string payload,
        string signatureBase64,
        string publicKey,
        IReadOnlyCollection<AsymmetricSignatureAlgorithm> allowedAlgorithms);
}

public sealed class AsymmetricSignatureVerifier : IAsymmetricSignatureVerifier
{
    /// <summary>
    /// The schemes <see cref="Verify"/> accepts. Deliberately excludes RSA-PSS: the webhook path
    /// has never accepted it, and widening what a payment callback will take is not a refactor.
    /// </summary>
    private static readonly IReadOnlyCollection<AsymmetricSignatureAlgorithm> LenientAlgorithms =
    [
        AsymmetricSignatureAlgorithm.RsaPkcs1Sha256,
        AsymmetricSignatureAlgorithm.EcdsaSha256Der,
        AsymmetricSignatureAlgorithm.EcdsaSha256P1363
    ];

    public bool Verify(string payload, string signatureBase64, string publicKey) =>
        VerifyWith(payload, signatureBase64, publicKey, LenientAlgorithms).IsValid;

    public SignatureVerificationResult VerifyWith(
        string payload,
        string signatureBase64,
        string publicKey,
        IReadOnlyCollection<AsymmetricSignatureAlgorithm> allowedAlgorithms)
    {
        var payloadBytes = Encoding.UTF8.GetBytes(payload);

        byte[] signatureBytes;
        try
        {
            signatureBytes = Convert.FromBase64String(signatureBase64);
        }
        catch (FormatException)
        {
            return new SignatureVerificationResult([], null, "invalid-base64");
        }

        // Lenient on purpose: the guard already refuses to boot on an unreadable key, and a lenient
        // read that fails to verify is harmless where a strict one would turn a bad signature into
        // a 500. PublicKeyFormat.TryRead is the strict reader both this and that guard share.
        if (!PublicKeyFormat.TryRead(publicKey, out var normalizedKey))
        {
            return new SignatureVerificationResult([], null, "unreadable-key");
        }

        var attempts = new List<SignatureAttempt>(allowedAlgorithms.Count);
        string? keyType = null;

        foreach (var algorithm in allowedAlgorithms)
        {
            var (isValid, imported, error) = TryVerify(algorithm, payloadBytes, signatureBytes, normalizedKey);

            if (imported)
            {
                keyType ??= KeyTypeOf(algorithm);
            }

            attempts.Add(new SignatureAttempt(algorithm, isValid, error));

            // The device paths pin exactly one scheme; the lenient path stops at the first match
            // just as the hand-written loops it replaced did.
            if (isValid)
            {
                break;
            }
        }

        return new SignatureVerificationResult(attempts, keyType, null);
    }

    /// <summary>
    /// The key family behind a public key, read the same way <see cref="VerifyWith"/> reads it.
    /// Null when the value is not a readable RSA or EC subject-public-key-info.
    /// </summary>
    public static string? DetectKeyType(string? publicKey)
    {
        if (!PublicKeyFormat.TryRead(publicKey, out var normalizedKey))
        {
            return null;
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(normalizedKey);
            return "RSA";
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
        }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(normalizedKey);
            return "ECDSA";
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
        }

        return null;
    }

    /// <summary>
    /// Whether <paramref name="algorithm"/> belongs to the key family of
    /// <paramref name="publicKey"/>. Used at enrollment so a caller cannot pin an EC key to an RSA
    /// scheme and leave the device permanently unable to sign in.
    /// </summary>
    public static bool AlgorithmMatchesKey(AsymmetricSignatureAlgorithm algorithm, string? publicKey)
    {
        var keyType = DetectKeyType(publicKey);

        return algorithm switch
        {
            AsymmetricSignatureAlgorithm.RsaPkcs1Sha256 or AsymmetricSignatureAlgorithm.RsaPssSha256 =>
                keyType == "RSA",
            AsymmetricSignatureAlgorithm.EcdsaSha256Der or AsymmetricSignatureAlgorithm.EcdsaSha256P1363 =>
                keyType == "ECDSA",
            _ => false
        };
    }

    private static string KeyTypeOf(AsymmetricSignatureAlgorithm algorithm) =>
        algorithm switch
        {
            AsymmetricSignatureAlgorithm.RsaPkcs1Sha256 or AsymmetricSignatureAlgorithm.RsaPssSha256 => "RSA",
            _ => "ECDSA"
        };

    private static (bool IsValid, bool Imported, string? Error) TryVerify(
        AsymmetricSignatureAlgorithm algorithm,
        byte[] payloadBytes,
        byte[] signatureBytes,
        string normalizedKey)
    {
        try
        {
            switch (algorithm)
            {
                case AsymmetricSignatureAlgorithm.RsaPkcs1Sha256:
                {
                    using var rsa = RSA.Create();
                    rsa.ImportFromPem(normalizedKey);
                    return (rsa.VerifyData(payloadBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), true, null);
                }

                case AsymmetricSignatureAlgorithm.RsaPssSha256:
                {
                    using var rsa = RSA.Create();
                    rsa.ImportFromPem(normalizedKey);
                    return (rsa.VerifyData(payloadBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss), true, null);
                }

                case AsymmetricSignatureAlgorithm.EcdsaSha256Der:
                {
                    using var ecdsa = ECDsa.Create();
                    ecdsa.ImportFromPem(normalizedKey);
                    return (ecdsa.VerifyData(payloadBytes, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence), true, null);
                }

                case AsymmetricSignatureAlgorithm.EcdsaSha256P1363:
                {
                    using var ecdsa = ECDsa.Create();
                    ecdsa.ImportFromPem(normalizedKey);
                    return (ecdsa.VerifyData(payloadBytes, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation), true, null);
                }

                default:
                    return (false, false, "unsupported-algorithm");
            }
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            // Wrong algorithm for this key (or an unreadable key) - the next scheme may still fit.
            return (false, false, ex.Message);
        }
    }
}

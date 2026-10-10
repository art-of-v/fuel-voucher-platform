using System.Security.Cryptography;
using System.Text;

namespace FuelFlow.SharedKernel.Security;

/// <summary>
/// Verifies asymmetric (RSA/ECDSA) signatures over an arbitrary UTF-8 payload.
/// Accepts a public key in three forms:
///   - PEM ("-----BEGIN PUBLIC KEY----- ..."),
///   - base64-encoded PEM (exactly what Monobank's GET /api/merchant/pubkey returns),
///   - raw base64 subject-public-key-info (DER) bytes.
/// Supports RSA SHA-256 PKCS#1 and ECDSA SHA-256 (DER and IEEE-P1363 encodings),
/// including the secp256k1 curve used by Monobank acquiring webhooks (works on Linux;
/// Windows CNG does not implement secp256k1).
/// </summary>
/// <remarks>
/// Which exception the key import raises depends on the platform, and the code has to survive both:
/// on Linux an EC key offered to <c>RSA.ImportFromPem</c> throws <see cref="ArgumentException"/>,
/// while the same call on Windows throws <see cref="CryptographicException"/>. Catching only one of
/// them made ECDSA-signed webhooks work on a developer machine and fail in production, where every
/// Monobank callback answered 500.
/// </remarks>
public interface IAsymmetricSignatureVerifier
{
    bool Verify(string payload, string signatureBase64, string publicKey);
}

public sealed class AsymmetricSignatureVerifier : IAsymmetricSignatureVerifier
{
    public bool Verify(string payload, string signatureBase64, string publicKey)
    {
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        byte[] signatureBytes;

        try
        {
            signatureBytes = Convert.FromBase64String(signatureBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        var normalizedKey = NormalizePublicKey(publicKey);
        if (normalizedKey is null)
        {
            return false;
        }

        // RSA first, then ECDSA, because the key's algorithm is not knowable from the PEM label -
        // both live under "PUBLIC KEY" (SubjectPublicKeyInfo).
        //
        // Offering an EC key to RSA is not a CryptographicException: the SPKI parses as a PEM but
        // cannot be read as RSA material, so ImportPem reports "no supported key formats" and throws
        // ArgumentException. Catching only CryptographicException let that escape the method, so the
        // ECDSA branch below never ran and every Monobank webhook died as a 500 - a paid order that
        // then never leaves PendingPayment.
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(normalizedKey);
            return rsa.VerifyData(payloadBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            // Wrong algorithm for this key (or an unreadable key) - fall through to ECDSA.
        }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(normalizedKey);

            bool valid = false;
            try
            {
                valid = ecdsa.VerifyData(payloadBytes, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            }
            catch (CryptographicException)
            {
            }

            if (!valid)
            {
                try
                {
                    valid = ecdsa.VerifyData(payloadBytes, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
                }
                catch (CryptographicException)
                {
                }
            }

            return valid;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string? NormalizePublicKey(string key)
    {
        key = key.Trim();
        if (key.StartsWith("-----"))
        {
            return key;
        }

        try
        {
            var decoded = Convert.FromBase64String(key);
            var text = Encoding.UTF8.GetString(decoded).Trim();
            if (text.StartsWith("-----"))
            {
                return text;
            }

            return new string(PemEncoding.Write("PUBLIC KEY", decoded));
        }
        catch (FormatException)
        {
            return $"-----BEGIN PUBLIC KEY-----\n{key}\n-----END PUBLIC KEY-----";
        }
    }
}

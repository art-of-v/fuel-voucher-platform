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

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(normalizedKey);
            return rsa.VerifyData(payloadBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (CryptographicException)
        {
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

using System.Security.Cryptography;
using System.Text;

namespace FuelFlow.SharedKernel.Security;

/// <summary>
/// Verifies asymmetric (RSA/ECDSA) signatures over an arbitrary UTF-8 payload against a PEM public key.
/// Supports RSA SHA-256 PKCS#1 and ECDSA SHA-256 (both DER and IEEE-P1363 encodings).
/// Used for device HMAC-style signatures and Monobank webhook X-Sign verification.
/// </summary>
public interface IAsymmetricSignatureVerifier
{
    bool Verify(string payload, string signatureBase64, string publicKeyPem);
}

public sealed class AsymmetricSignatureVerifier : IAsymmetricSignatureVerifier
{
    public bool Verify(string payload, string signatureBase64, string publicKeyPem)
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

        var pemKey = publicKeyPem.Trim();
        if (!pemKey.StartsWith("-----"))
        {
            pemKey = $"-----BEGIN PUBLIC KEY-----\n{pemKey}\n-----END PUBLIC KEY-----";
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(pemKey);
            return rsa.VerifyData(payloadBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (CryptographicException)
        {
        }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(pemKey);

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
}

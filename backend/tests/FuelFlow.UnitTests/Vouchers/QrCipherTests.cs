using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using FuelFlow.Features.Vouchers.Security;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Options;
using Xunit;

namespace FuelFlow.UnitTests.Vouchers;

/// <summary>
/// What the QR cipher actually guarantees, as opposed to what its name suggests.
/// </summary>
/// <remarks>
/// These are unit tests on purpose: the properties that matter - that the plaintext is not
/// recoverable without the key, that a tampered column is refused, that the same input encrypts
/// differently each time - are properties of the cipher, and they hold or fail without a database.
/// The round-trip through PostgreSQL, and the fact that the column really is opaque to SQL, are
/// covered in the integration suite.
/// </remarks>
public sealed class QrCipherTests
{
    private static QrCipher CipherFor(params byte[] keyBytes)
        => new(Options.Create(new QrCipherOptions
        {
            Key = Convert.ToBase64String(keyBytes.Length == 0 ? Enumerable.Repeat((byte)0x11, 32).ToArray() : keyBytes)
        }));

    private static QrCipher Cipher() => CipherFor();

    private const string Qr = "BRN:4400720039202603121834256789ZZ4000123456789";

    [Fact]
    public void Encrypted_RoundTrips_BackToTheOriginalPayload()
    {
        var cipher = Cipher();

        cipher.Decrypt(cipher.Encrypt(Qr)).Should().Be(Qr);
    }

    [Fact]
    public void The_Stored_Bytes_Do_Not_Contain_The_Payload()
    {
        var stored = Cipher().Encrypt(Qr);

        // The plaintext as UTF-8 appears nowhere in the ciphertext. AES-GCM is not obfuscation, so
        // this is a property of the mode rather than luck - but it is the property a reviewer is
        // actually asking about when they ask whether the column is "just base64".
        var plainBytes = Encoding.UTF8.GetBytes(Qr);

        IndexOf(stored, plainBytes).Should().Be(-1);
        Encoding.UTF8.GetString(stored).Should().NotContain(Qr);
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                return i;
            }
        }

        return -1;
    }

    [Fact]
    public void The_Same_Payload_Encrypts_Differently_Every_Time()
    {
        // AES-GCM takes a fresh nonce per encryption, so identical input must not produce identical
        // bytes. This is also the property that makes the ciphertext unusable for deduplication, and
        // the reason QrPayloadSha256 exists at all.
        var cipher = Cipher();

        var first = cipher.Encrypt(Qr);
        var second = cipher.Encrypt(Qr);

        first.Should().NotEqual(second);
    }

    [Fact]
    public void Another_Key_Cannot_Read_It()
    {
        var stored = CipherFor(Enumerable.Repeat((byte)0x11, 32).ToArray()).Encrypt(Qr);
        var impostor = CipherFor(Enumerable.Repeat((byte)0x22, 32).ToArray());

        var act = () => impostor.Decrypt(stored);

        act.Should().Throw<CryptographicException>(
            "this is the failure an operator hits if the deployment key drifts from the data");
    }

    [Fact]
    public void ATampered_Column_Is_Refused()
    {
        var stored = Cipher().Encrypt(Qr);

        // Flip a bit inside the ciphertext, past the nonce and tag.
        stored[^1] ^= 0x01;

        var act = () => Cipher().Decrypt(stored);

        act.Should().Throw<CryptographicException>(
            "GCM authenticates as well as encrypts: a modified row must not verify");
    }

    [Fact]
    public void The_Fingerprint_Is_Stable_And_Keyless()
    {
        var a = CipherFor(Enumerable.Repeat((byte)0x11, 32).ToArray());
        var b = CipherFor(Enumerable.Repeat((byte)0x22, 32).ToArray());

        a.Sha256(Qr).Should().Equal(b.Sha256(Qr));
    }

    [Fact]
    public void The_Fingerprint_Is_Sha256_Of_The_Payload()
    {
        Cipher().Sha256(Qr).Should().Equal(SHA256.HashData(Encoding.UTF8.GetBytes(Qr)));
    }

    [Fact]
    public void A_Missing_Key_Is_Refused_At_Construction()
    {
        var act = () => new QrCipher(Options.Create(new QrCipherOptions()));

        act.Should().Throw<InvalidOperationException>(
            "a defaulted key is a key every deployment shares, so the app must refuse to start");
    }

    [Fact]
    public void A_Key_Of_The_Wrong_Length_Is_Refused()
    {
        // Sixteen bytes is AES-128, not the AES-256 this uses. Silently accepting it would encrypt
        // vouchers under a weaker key than the one an operator believes is in force, and would make
        // a QR unrecoverable the moment the real key arrived.
        var act = () => CipherFor(Enumerable.Repeat((byte)0x11, 16).ToArray());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_Hex_Key_Is_Accepted()
    {
        var hex = Convert.ToHexString(Enumerable.Repeat((byte)0x2A, 32).ToArray());

        var cipher = new QrCipher(Options.Create(new QrCipherOptions { Key = hex }));

        cipher.Decrypt(cipher.Encrypt(Qr)).Should().Be(Qr);
    }

    [Fact]
    public void A_Column_Too_Short_To_Be_Ours_Is_Refused_Not_Garbage()
    {
        var act = () => Cipher().Decrypt([0x01, 0x02, 0x03]);

        act.Should().Throw<CryptographicException>(
            "a short column means something else wrote it; a wrong-length read should say so");
    }

    [Fact]
    public void A_Non_Ascii_Payload_RoundTrips()
    {
        // Voucher payloads are not guaranteed to be ASCII, and UTF-8 length differs from character
        // count - a truncation bug here would corrupt a payload that looks fine in the admin.
        const string payload = "QR-тест-😀-payload";

        Cipher().Decrypt(Cipher().Encrypt(payload)).Should().Be(payload);
    }
}
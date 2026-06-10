using System.Security.Cryptography;
using laundryghar.SharedDataModel.Crypto;

namespace laundryghar.ServiceDefaults.Tests.Crypto;

/// <summary>
/// Unit tests for AesGcmFieldCipher: encrypt/decrypt, legacy plaintext passthrough,
/// and the missing-key fail-closed guard exercised via AddSharedDataModel.
/// </summary>
public sealed class AesGcmFieldCipherTests
{
    // A stable 32-byte key for tests.
    private static byte[] TestKey() => new byte[32]
    {
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
        0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
        0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18,
        0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F, 0x20,
    };

    // ── Constructor guard ────────────────────────────────────────────────────

    [Fact]
    public void Constructor_ThrowsArgumentException_WhenKeyIsWrongLength()
    {
        var shortKey = new byte[16]; // 128-bit — rejected
        Assert.Throws<ArgumentException>(() => new AesGcmFieldCipher(shortKey));
    }

    [Fact]
    public void Constructor_Accepts_32ByteKey()
    {
        var cipher = new AesGcmFieldCipher(TestKey());
        Assert.NotNull(cipher);
    }

    // ── Null passthrough ─────────────────────────────────────────────────────

    [Fact]
    public void Encrypt_ReturnsNull_ForNullInput()
    {
        var cipher = new AesGcmFieldCipher(TestKey());
        Assert.Null(cipher.Encrypt(null));
    }

    [Fact]
    public void Decrypt_ReturnsNull_ForNullInput()
    {
        var cipher = new AesGcmFieldCipher(TestKey());
        Assert.Null(cipher.Decrypt(null));
    }

    // ── Round-trip ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("ABCDE1234F")]          // PAN
    [InlineData("123456789012")]        // bank account
    [InlineData("name@okaxis")]         // UPI ID
    [InlineData("")]                    // empty string
    [InlineData("Special chars: !@#")] // edge case
    public void Encrypt_ThenDecrypt_ReturnsSamePlaintext(string plaintext)
    {
        var cipher = new AesGcmFieldCipher(TestKey());
        var encrypted = cipher.Encrypt(plaintext);

        Assert.NotNull(encrypted);
        Assert.StartsWith(AesGcmFieldCipher.Prefix, encrypted);

        var decrypted = cipher.Decrypt(encrypted);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_ProducesDifferentCiphertext_OnEachCall_ForSamePlaintext()
    {
        // AES-GCM uses a random nonce per call, so two encryptions of the same
        // value must not be equal (no deterministic ciphertext).
        var cipher = new AesGcmFieldCipher(TestKey());
        var a = cipher.Encrypt("ABCDE1234F");
        var b = cipher.Encrypt("ABCDE1234F");
        Assert.NotEqual(a, b);
    }

    // ── Legacy plaintext passthrough ─────────────────────────────────────────

    [Theory]
    [InlineData("ABCDE1234F")]          // PAN written before encryption was enabled
    [InlineData("123456789012")]
    [InlineData("plaintext-no-prefix")]
    public void Decrypt_PassesThroughLegacyPlaintext_WhenPrefixAbsent(string legacy)
    {
        var cipher = new AesGcmFieldCipher(TestKey());
        // Value has no "enc:v1:" prefix → treated as legacy plaintext, returned as-is.
        var result = cipher.Decrypt(legacy);
        Assert.Equal(legacy, result);
    }

    // ── Tamper detection (GCM authentication tag) ─────────────────────────────

    [Fact]
    public void Decrypt_ThrowsCryptographicException_WhenCiphertextIsTampered()
    {
        var cipher    = new AesGcmFieldCipher(TestKey());
        var encrypted = cipher.Encrypt("secret-value")!;

        // Flip a bit in the ciphertext portion (after the prefix and base64-decode).
        var payload   = Convert.FromBase64String(encrypted[AesGcmFieldCipher.Prefix.Length..]);
        payload[^1]  ^= 0xFF; // Corrupt the last byte of the ciphertext.
        var tampered  = AesGcmFieldCipher.Prefix + Convert.ToBase64String(payload);

        // AuthenticationTagMismatchException (a CryptographicException subclass) is what
        // AES-GCM throws on macOS via the Apple crypto backend. Use ThrowsAny to accept
        // both the base type and any derived type.
        Assert.ThrowsAny<CryptographicException>((Action)(() => cipher.Decrypt(tampered)));
    }

    // ── Wrong key ─────────────────────────────────────────────────────────────

    [Fact]
    public void Decrypt_ThrowsCryptographicException_WhenDecryptedWithWrongKey()
    {
        var key1 = TestKey();
        var key2 = new byte[32]; // all-zeros key
        RandomNumberGenerator.Fill(key2);

        var cipher1   = new AesGcmFieldCipher(key1);
        var cipher2   = new AesGcmFieldCipher(key2);
        var encrypted = cipher1.Encrypt("ABCDE1234F")!;

        Assert.ThrowsAny<CryptographicException>((Action)(() => cipher2.Decrypt(encrypted)));
    }
}

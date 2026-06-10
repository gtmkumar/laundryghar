using System.Security.Cryptography;
using laundryghar.Identity.Infrastructure.Auth;

namespace laundryghar.Identity.Tests.Auth;

/// <summary>
/// Unit tests for <see cref="OtpSecurityHelper"/>:
///   - HMAC-SHA256 hash round-trip and non-determinism
///   - VerifyCode happy path (salted)
///   - VerifyCode legacy fallback (null salt → SHA-256)
///   - VerifyCode rejects wrong code
///   - Lockout threshold logic
///   - ResolveHmacKey dev fallback and prod fail-closed
/// </summary>
public sealed class OtpSecurityHelperTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static byte[] MakeKey()
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        return key;
    }

    private static OtpSettings DefaultSettings(int threshold = 10, int windowMin = 15, int durationMin = 15)
        => new OtpSettings
        {
            LockoutThreshold       = threshold,
            LockoutWindowMinutes   = windowMin,
            LockoutDurationMinutes = durationMin
        };

    // ── GenerateSalt ─────────────────────────────────────────────────────────

    [Fact]
    public void GenerateSalt_ReturnsDifferentValues_OnEachCall()
    {
        var a = OtpSecurityHelper.GenerateSalt();
        var b = OtpSecurityHelper.GenerateSalt();
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void GenerateSalt_Returns32CharHex()
    {
        var salt = OtpSecurityHelper.GenerateSalt();
        Assert.Equal(32, salt.Length);
        Assert.Matches("^[0-9a-f]+$", salt);
    }

    // ── ComputeHmac ──────────────────────────────────────────────────────────

    [Fact]
    public void ComputeHmac_IsDeterministicForSameSaltAndCode()
    {
        var key  = MakeKey();
        var salt = OtpSecurityHelper.GenerateSalt();
        var h1   = OtpSecurityHelper.ComputeHmac(key, salt, "123456");
        var h2   = OtpSecurityHelper.ComputeHmac(key, salt, "123456");
        Assert.Equal(h1, h2);
    }

    [Fact]
    public void ComputeHmac_DiffersForDifferentSalts()
    {
        var key   = MakeKey();
        var salt1 = OtpSecurityHelper.GenerateSalt();
        var salt2 = OtpSecurityHelper.GenerateSalt();
        var h1    = OtpSecurityHelper.ComputeHmac(key, salt1, "123456");
        var h2    = OtpSecurityHelper.ComputeHmac(key, salt2, "123456");
        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void ComputeHmac_DiffersForDifferentCodes()
    {
        var key  = MakeKey();
        var salt = OtpSecurityHelper.GenerateSalt();
        var h1   = OtpSecurityHelper.ComputeHmac(key, salt, "123456");
        var h2   = OtpSecurityHelper.ComputeHmac(key, salt, "654321");
        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void ComputeHmac_ReturnsLowerHex_64Chars()
    {
        var hash = OtpSecurityHelper.ComputeHmac(MakeKey(), OtpSecurityHelper.GenerateSalt(), "000000");
        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]+$", hash);
    }

    // ── VerifyCode — salted path ─────────────────────────────────────────────

    [Fact]
    public void VerifyCode_ReturnsTrue_ForCorrectCode_SaltedPath()
    {
        var key      = MakeKey();
        var salt     = OtpSecurityHelper.GenerateSalt();
        var code     = "482193";
        var stored   = OtpSecurityHelper.ComputeHmac(key, salt, code);
        Assert.True(OtpSecurityHelper.VerifyCode(key, salt, stored, code));
    }

    [Fact]
    public void VerifyCode_ReturnsFalse_ForWrongCode_SaltedPath()
    {
        var key    = MakeKey();
        var salt   = OtpSecurityHelper.GenerateSalt();
        var stored = OtpSecurityHelper.ComputeHmac(key, salt, "111111");
        Assert.False(OtpSecurityHelper.VerifyCode(key, salt, stored, "999999"));
    }

    // ── VerifyCode — legacy (null salt) fallback ─────────────────────────────

    [Fact]
    public void VerifyCode_ReturnsTrue_ForCorrectCode_LegacyPath()
    {
        // Simulate a row written before the salt migration: code_salt IS NULL,
        // code_hash is the old unsalted SHA-256.
        var key        = MakeKey();
        var code       = "384756";
        var legacyHash = OtpSecurityHelper.ComputeLegacySha256(code);

        // storedSalt == null → legacy path
        Assert.True(OtpSecurityHelper.VerifyCode(key, null, legacyHash, code));
    }

    [Fact]
    public void VerifyCode_ReturnsFalse_ForWrongCode_LegacyPath()
    {
        var key        = MakeKey();
        var legacyHash = OtpSecurityHelper.ComputeLegacySha256("111111");
        Assert.False(OtpSecurityHelper.VerifyCode(key, null, legacyHash, "222222"));
    }

    [Fact]
    public void VerifyCode_SaltedPath_DoesNotMatchLegacyHash()
    {
        // A salted row must NOT verify against the legacy hash, even with the same code.
        var key        = MakeKey();
        var code       = "123456";
        var legacyHash = OtpSecurityHelper.ComputeLegacySha256(code);
        var salt       = OtpSecurityHelper.GenerateSalt();
        // Pass a non-null salt but the stored hash is actually the legacy SHA-256 value
        Assert.False(OtpSecurityHelper.VerifyCode(key, salt, legacyHash, code));
    }

    // ── Lockout threshold ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(new short[] { 3, 3, 3 }, 10, false)]  // 9 < 10
    [InlineData(new short[] { 3, 3, 4 }, 10, true)]   // 10 == threshold
    [InlineData(new short[] { 5, 5, 5 }, 10, true)]   // 15 > 10
    [InlineData(new short[] { },          10, false)]  // no rows
    [InlineData(new short[] { 0, 0, 0 }, 10, false)]  // all zero
    public void ExceedsLockoutThreshold_CorrectlyEvaluatesSum(short[] attempts, int threshold, bool expected)
    {
        var total  = OtpSecurityHelper.SumWindowAttempts(attempts);
        var result = OtpSecurityHelper.ExceedsLockoutThreshold(total, threshold);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void SumWindowAttempts_HandlesEmptySequence()
    {
        Assert.Equal(0, OtpSecurityHelper.SumWindowAttempts([]));
    }

    [Fact]
    public void SumWindowAttempts_SumsCorrectly()
    {
        Assert.Equal(7, OtpSecurityHelper.SumWindowAttempts([2, 3, 2]));
    }

    // ── ResolveHmacKey ────────────────────────────────────────────────────────

    [Fact]
    public void ResolveHmacKey_UsesDevFallback_WhenHmacKeyNull_AndDevelopment()
    {
        var settings = new OtpSettings { HmacKey = null };
        var key = OtpSecurityHelper.ResolveHmacKey(settings, isDevelopment: true);
        Assert.NotNull(key);
        Assert.Equal(32, key.Length);
    }

    [Fact]
    public void ResolveHmacKey_UsesDevFallback_WhenHmacKeyEmpty_AndDevelopment()
    {
        var settings = new OtpSettings { HmacKey = "" };
        var key = OtpSecurityHelper.ResolveHmacKey(settings, isDevelopment: true);
        Assert.NotNull(key);
        Assert.Equal(32, key.Length);
    }

    [Fact]
    public void ResolveHmacKey_ThrowsInvalidOperation_WhenHmacKeyNull_AndNotDevelopment()
    {
        var settings = new OtpSettings { HmacKey = null };
        Assert.Throws<InvalidOperationException>(
            () => OtpSecurityHelper.ResolveHmacKey(settings, isDevelopment: false));
    }

    [Fact]
    public void ResolveHmacKey_DecodesBase64Key_WhenProvided()
    {
        var rawKey    = new byte[32];
        RandomNumberGenerator.Fill(rawKey);
        var settings  = new OtpSettings { HmacKey = Convert.ToBase64String(rawKey) };
        var resolved  = OtpSecurityHelper.ResolveHmacKey(settings, isDevelopment: false);
        Assert.Equal(rawKey, resolved);
    }

    // ── Dev fallback key is stable (same key across calls in same process) ───

    [Fact]
    public void ResolveHmacKey_DevFallback_IsStable_AcrossCalls()
    {
        var settings = new OtpSettings { HmacKey = null };
        var key1 = OtpSecurityHelper.ResolveHmacKey(settings, isDevelopment: true);
        var key2 = OtpSecurityHelper.ResolveHmacKey(settings, isDevelopment: true);
        Assert.Equal(key1, key2);
    }
}

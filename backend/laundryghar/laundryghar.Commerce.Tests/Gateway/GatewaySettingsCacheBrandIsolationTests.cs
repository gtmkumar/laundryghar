using System.Security.Cryptography;
using System.Text.Json;
using laundryghar.Commerce.Infrastructure.Gateway;
using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Crypto;
using laundryghar.SharedDataModel.Entities.Kernel;
using laundryghar.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace laundryghar.Commerce.Tests.Gateway;

/// <summary>
/// SEC-2 regression tests: <see cref="GatewaySettingsCache"/> must isolate gateway
/// credentials PER BRAND. The old singleton cached ONE decrypted key/secret/webhook-secret
/// for 60s and served it to every brand — so brand B's payment within the TTL window could
/// transact against brand A's Razorpay credentials.
///
/// These tests use a real <see cref="LaundryGharDbContext"/> on the EF InMemory provider so
/// the actual per-brand DB query + cache keying are exercised end to end.
/// </summary>
public sealed class GatewaySettingsCacheBrandIsolationTests
{
    private static readonly Guid BrandA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BrandB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static IFieldCipher NewCipher()
        => new AesGcmFieldCipher(RandomNumberGenerator.GetBytes(32));

    private static LaundryGharDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<LaundryGharDbContext>()
            .UseInMemoryDatabase($"gw-cache-{Guid.NewGuid()}")
            .EnableServiceProviderCaching(false)
            .Options;
        return new LaundryGharDbContext(opts);
    }

    private static SystemSetting GatewayRow(Guid? brandId, string keyId, string keySecretPlain, IFieldCipher cipher)
    {
        var settings = new PaymentGatewaySettings
        {
            Provider      = "razorpay",
            Enabled       = true,
            KeyId         = keyId,
            KeySecret     = cipher.Encrypt(keySecretPlain),
            WebhookSecret = cipher.Encrypt($"wh-{keyId}"),
        };
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        return new SystemSetting
        {
            Id           = Guid.NewGuid(),
            BrandId      = brandId,
            ScopeType    = brandId is null ? "global" : "brand",
            Category     = "payment",
            SettingKey   = "gateway",
            SettingValue = json,
            DataType     = "json",
            Status       = "active",
            CreatedAt    = DateTimeOffset.UtcNow,
            UpdatedAt    = DateTimeOffset.UtcNow,
            Version      = 1,
        };
    }

    // ── Brand A priming must NOT bleed into brand B ───────────────────────────

    [Fact]
    public async Task BrandAPrime_DoesNotServe_BrandBCredentials()
    {
        var cipher = NewCipher();
        await using var db = NewDb();

        db.SystemSettings.Add(GatewayRow(BrandA, "rzp_A", "secret_A", cipher));
        db.SystemSettings.Add(GatewayRow(BrandB, "rzp_B", "secret_B", cipher));
        await db.SaveChangesAsync();

        var cache = new GatewaySettingsCache(cipher, TimeSpan.FromSeconds(60));

        // Prime brand A first (this is what used to poison the singleton).
        var a = await cache.GetAsync(db, BrandA, default);
        Assert.Equal("rzp_A",    a.KeyId);
        Assert.Equal("secret_A", a.KeySecret);

        // Within the TTL window, brand B must get its OWN credentials — not A's.
        var b = await cache.GetAsync(db, BrandB, default);
        Assert.Equal("rzp_B",    b.KeyId);
        Assert.Equal("secret_B", b.KeySecret);
        Assert.Equal("wh-rzp_B", b.WebhookSecret);
    }

    // ── Per-brand caching: a brand sees its own cached entry on a second call ──

    [Fact]
    public async Task SameBrand_SecondCall_ReturnsCachedEntry()
    {
        var cipher = NewCipher();
        await using var db = NewDb();
        db.SystemSettings.Add(GatewayRow(BrandA, "rzp_A", "secret_A", cipher));
        await db.SaveChangesAsync();

        var cache = new GatewaySettingsCache(cipher, TimeSpan.FromSeconds(60));

        var first  = await cache.GetAsync(db, BrandA, default);
        var second = await cache.GetAsync(db, BrandA, default);

        Assert.Equal("rzp_A", first.KeyId);
        Assert.Equal("rzp_A", second.KeyId);
    }

    // ── Brand-specific row wins over the global row ───────────────────────────

    [Fact]
    public async Task BrandSpecificRow_WinsOver_GlobalRow()
    {
        var cipher = NewCipher();
        await using var db = NewDb();
        db.SystemSettings.Add(GatewayRow(null,   "rzp_GLOBAL", "secret_GLOBAL", cipher));
        db.SystemSettings.Add(GatewayRow(BrandA, "rzp_A",      "secret_A",      cipher));
        await db.SaveChangesAsync();

        var cache = new GatewaySettingsCache(cipher, TimeSpan.FromSeconds(60));

        var a = await cache.GetAsync(db, BrandA, default);
        Assert.Equal("rzp_A", a.KeyId); // brand row, not global
    }

    // ── Brand with no own row falls back to the global config row ─────────────

    [Fact]
    public async Task BrandWithoutOwnRow_FallsBackTo_GlobalRow()
    {
        var cipher = NewCipher();
        await using var db = NewDb();
        db.SystemSettings.Add(GatewayRow(null,   "rzp_GLOBAL", "secret_GLOBAL", cipher));
        db.SystemSettings.Add(GatewayRow(BrandA, "rzp_A",      "secret_A",      cipher));
        await db.SaveChangesAsync();

        var cache = new GatewaySettingsCache(cipher, TimeSpan.FromSeconds(60));

        // Brand B has no own row → must resolve the global row, NOT brand A's.
        var b = await cache.GetAsync(db, BrandB, default);
        Assert.Equal("rzp_GLOBAL", b.KeyId);
        Assert.Equal("secret_GLOBAL", b.KeySecret);
    }

    // ── Webhook lane resolves settings by the matched payment's brand ─────────

    [Fact]
    public async Task WebhookBrandResolution_UsesPaymentBrand_NotPrimedBrand()
    {
        // Simulates the webhook path: an earlier (authenticated) call primed brand A; the
        // webhook then resolves by the matched payment's brand (B) and must get B's secret.
        var cipher = NewCipher();
        await using var db = NewDb();
        db.SystemSettings.Add(GatewayRow(BrandA, "rzp_A", "secret_A", cipher));
        db.SystemSettings.Add(GatewayRow(BrandB, "rzp_B", "secret_B", cipher));
        await db.SaveChangesAsync();

        var cache = new GatewaySettingsCache(cipher, TimeSpan.FromSeconds(60));

        _ = await cache.GetAsync(db, BrandA, default); // prime A (whoever called last)

        // Webhook handler calls GetAsync(db, payment.BrandId, ct) — here payment.BrandId == BrandB.
        var resolved = await cache.GetAsync(db, BrandB, default);
        Assert.Equal("wh-rzp_B", resolved.WebhookSecret);
    }
}

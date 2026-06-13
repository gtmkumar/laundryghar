using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Crypto;

namespace laundryghar.Orders.Tests.Pricing;

/// <summary>
/// Unit tests for the signed fare-quote token: round-trips, expiry, and tamper rejection.
/// Uses a real AES-GCM cipher with a fixed test key.
/// </summary>
public sealed class FareQuoteTokenTests
{
    private static IFieldCipher Cipher() => new AesGcmFieldCipher(new byte[32]
    {
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
        0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
        0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18,
        0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F, 0x20,
    });

    private static readonly DateTimeOffset Now = new(2026, 6, 13, 12, 0, 0, TimeSpan.Zero);

    private static FareQuotePayload SamplePayload(long expiresAtUnix) => new(
        PickupAddressId: Guid.NewGuid(),
        DeliveryAddressId: Guid.NewGuid(),
        Tier: "two_wheeler",
        DistanceKm: 5.2m,
        PickupCharge: 15m,
        DeliveryCharge: 65m,
        SurgeMultiplier: 1m,
        ExpiresAtUnix: expiresAtUnix);

    [Fact]
    public void Issue_Then_Verify_RoundTrips()
    {
        var cipher = Cipher();
        var payload = SamplePayload(Now.AddMinutes(10).ToUnixTimeSeconds());

        var token = FareQuoteToken.Issue(cipher, payload);
        var back  = FareQuoteToken.Verify(cipher, token, Now);

        Assert.NotNull(back);
        Assert.Equal(payload.PickupAddressId, back!.PickupAddressId);
        Assert.Equal(payload.DeliveryCharge, back.DeliveryCharge);
        Assert.Equal(payload.Tier, back.Tier);
    }

    [Fact]
    public void Verify_ReturnsNull_WhenExpired()
    {
        var cipher = Cipher();
        var token  = FareQuoteToken.Issue(cipher, SamplePayload(Now.AddMinutes(-1).ToUnixTimeSeconds()));
        Assert.Null(FareQuoteToken.Verify(cipher, token, Now));
    }

    [Fact]
    public void Verify_ReturnsNull_WhenTampered()
    {
        var cipher = Cipher();
        var token  = FareQuoteToken.Issue(cipher, SamplePayload(Now.AddMinutes(10).ToUnixTimeSeconds()));
        var tampered = token + "x";
        Assert.Null(FareQuoteToken.Verify(cipher, tampered, Now));
    }

    [Fact]
    public void Verify_ReturnsNull_ForNullOrEmpty()
    {
        var cipher = Cipher();
        Assert.Null(FareQuoteToken.Verify(cipher, null, Now));
        Assert.Null(FareQuoteToken.Verify(cipher, "", Now));
    }

    [Fact]
    public void Verify_ReturnsNull_ForForeignKey()
    {
        // A token issued under one key must not validate under another.
        var issuer   = Cipher();
        var attacker = new AesGcmFieldCipher(new byte[32]); // all-zero key — different
        var token    = FareQuoteToken.Issue(issuer, SamplePayload(Now.AddMinutes(10).ToUnixTimeSeconds()));
        Assert.Null(FareQuoteToken.Verify(attacker, token, Now));
    }
}

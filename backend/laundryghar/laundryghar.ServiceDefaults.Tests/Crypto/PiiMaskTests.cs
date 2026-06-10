using laundryghar.SharedDataModel.Crypto;

namespace laundryghar.ServiceDefaults.Tests.Crypto;

public sealed class PiiMaskTests
{
    // ── MaskPan ──────────────────────────────────────────────────────────────

    [Fact]
    public void MaskPan_ReturnsNull_ForNull() =>
        Assert.Null(PiiMask.MaskPan(null));

    [Fact]
    public void MaskPan_MasksFirst5Chars_Of10CharPan()
    {
        var result = PiiMask.MaskPan("ABCDE1234F");
        Assert.Equal("XXXXX1234F", result);
    }

    [Fact]
    public void MaskPan_HandlesShortValues()
    {
        // Shorter than 5 chars → fully masked
        var result = PiiMask.MaskPan("ABC");
        Assert.Equal("XXX", result);
    }

    [Fact]
    public void MaskPan_ReturnsEmpty_ForEmpty() =>
        Assert.Equal("", PiiMask.MaskPan(""));

    // ── MaskBankAccount ───────────────────────────────────────────────────────

    [Fact]
    public void MaskBankAccount_ReturnsNull_ForNull() =>
        Assert.Null(PiiMask.MaskBankAccount(null));

    [Fact]
    public void MaskBankAccount_ShowsLast4_ForNormalAccount()
    {
        var result = PiiMask.MaskBankAccount("123456789012");
        Assert.Equal("••••••••9012", result);
    }

    [Fact]
    public void MaskBankAccount_HandlesShortValues()
    {
        var result = PiiMask.MaskBankAccount("123");
        Assert.Equal("•••", result);
    }

    [Fact]
    public void MaskBankAccount_Shows_AllDigits_When4Chars()
    {
        // 4-char account: nothing to mask, last 4 = entire value
        var result = PiiMask.MaskBankAccount("1234");
        Assert.Equal("1234", result);
    }

    [Fact]
    public void MaskBankAccount_Masks_AllButLast4_When5Chars()
    {
        var result = PiiMask.MaskBankAccount("12345");
        Assert.Equal("•2345", result);
    }

    // ── MaskUpi ───────────────────────────────────────────────────────────────

    [Fact]
    public void MaskUpi_ReturnsNull_ForNull() =>
        Assert.Null(PiiMask.MaskUpi(null));

    [Fact]
    public void MaskUpi_ReplacesLocalPart_RetainsDomain()
    {
        var result = PiiMask.MaskUpi("name@okaxis");
        Assert.Equal("••••@okaxis", result);
    }

    [Fact]
    public void MaskUpi_MasksWholeThing_WhenNoAtSign()
    {
        var result = PiiMask.MaskUpi("noemail");
        Assert.Equal("••••", result);
    }

    [Fact]
    public void MaskUpi_ReturnsEmpty_ForEmpty() =>
        Assert.Equal("", PiiMask.MaskUpi(""));
}

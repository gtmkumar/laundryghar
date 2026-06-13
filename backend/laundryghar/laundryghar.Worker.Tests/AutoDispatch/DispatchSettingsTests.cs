using laundryghar.SharedDataModel.Common;

namespace laundryghar.Worker.Tests.AutoDispatch;

/// <summary>
/// Policy tests for <see cref="DispatchSettings.Normalize"/>: offer_accept is platform-only;
/// every other scope/mode resolves to push (safe default).
/// </summary>
public sealed class DispatchSettingsTests
{
    [Fact]
    public void OfferAccept_AllowedOnlyAtPlatformScope()
    {
        Assert.Equal(DispatchSettings.ModeOfferAccept,
            DispatchSettings.Normalize(DispatchSettings.ModeOfferAccept, isPlatformScope: true));

        Assert.Equal(DispatchSettings.ModePush,
            DispatchSettings.Normalize(DispatchSettings.ModeOfferAccept, isPlatformScope: false));
    }

    [Theory]
    [InlineData("push", true)]
    [InlineData("push", false)]
    [InlineData(null, true)]
    [InlineData("garbage", true)]
    public void NonOfferAccept_AlwaysResolvesToPush(string? mode, bool platform)
        => Assert.Equal(DispatchSettings.ModePush, DispatchSettings.Normalize(mode, platform));
}

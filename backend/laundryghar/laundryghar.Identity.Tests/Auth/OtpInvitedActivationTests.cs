using laundryghar.Identity.Application.Auth.Commands;
using laundryghar.SharedDataModel.Enums;

namespace laundryghar.Identity.Tests.Auth;

/// <summary>
/// DEFECT 8 — unit tests for the account-status decision applied on a successful OTP
/// verification (<see cref="OtpVerifyHandler.ResolveStatusOnVerify"/>):
///   • invited → active (first OTP login completes the invite)
///   • active stays active
///   • suspended / deleted / locked are blocked from signing in
/// </summary>
public sealed class OtpInvitedActivationTests
{
    [Fact]
    public void Invited_IsActivated_NotBlocked()
    {
        var (blocked, next) = OtpVerifyHandler.ResolveStatusOnVerify(UserStatus.Invited);
        Assert.False(blocked);
        Assert.Equal(UserStatus.Active, next);
    }

    [Fact]
    public void Active_StaysActive_NotBlocked()
    {
        var (blocked, next) = OtpVerifyHandler.ResolveStatusOnVerify(UserStatus.Active);
        Assert.False(blocked);
        Assert.Equal(UserStatus.Active, next);
    }

    [Theory]
    [InlineData(UserStatus.Suspended)]
    [InlineData(UserStatus.Deleted)]
    [InlineData(UserStatus.Locked)]
    public void DeactivatedAccounts_AreBlocked_AndNotActivated(string status)
    {
        var (blocked, next) = OtpVerifyHandler.ResolveStatusOnVerify(status);
        Assert.True(blocked);
        Assert.Equal(status, next); // status is never silently changed for a blocked account
    }
}

using System.Security.Claims;
using laundryghar.Utilities.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace operations.Tests.Auth;

/// <summary>
/// Regression guard for a real, silent outage in step-up (docs/rbac.md §8).
///
/// <para>The JWT bearer handler runs with inbound claim mapping ON (the ASP.NET default), which
/// rewrites the token's registered claims to their long WS-Fed URIs before the principal is built:
/// <c>sub</c> → <see cref="ClaimTypes.NameIdentifier"/>, <c>email</c> → <see cref="ClaimTypes.Email"/>.
/// The token really does carry <c>email</c>, but by the time <c>HttpContextCurrentUser</c> reads it
/// the short name is gone.</para>
///
/// <para>The effect was not a visible error anywhere near the cause: <c>ICurrentUser.Email</c>
/// returned null for every caller, and <c>StepUpVerifyHandler</c> — which derives the OTP identifier
/// from it — threw "No email on file to verify against." Since step-up gates every
/// <c>high</c>/<c>critical</c> permission, that made <c>payment.refund</c>, <c>pricing.publish</c>,
/// <c>user.create</c> and <c>brands.update</c> ALL unreachable. Reproduced live against a running
/// core host on 2026-08-24 while verifying custom domains (docs/TASKS.md T-10).</para>
///
/// <para>These tests drive the mapped shape a real bearer token produces, which is what the earlier
/// tests missed: <c>RbacTestSupport.Principal</c> builds short-named claims only, so it can never
/// have caught this.</para>
/// </summary>
public class CurrentUserClaimMappingTests
{
    // 1 ── the actual bug: mapped claim names must still resolve.
    [Fact]
    public void email_and_phone_resolve_from_the_mapped_claim_types()
    {
        var user = CurrentUser(
            new Claim(ClaimTypes.Email, "admin@laundryghar.local"),
            new Claim(ClaimTypes.MobilePhone, "+919999999999"));

        Assert.Equal("admin@laundryghar.local", user.Email);
        Assert.Equal("+919999999999", user.Phone);
    }

    // 2 ── unmapped tokens (mapping disabled, or a non-registered claim) keep working unchanged.
    [Fact]
    public void email_and_phone_still_resolve_from_the_raw_claim_names()
    {
        var user = CurrentUser(
            new Claim("email", "raw@laundryghar.local"),
            new Claim("phone", "+911111111111"));

        Assert.Equal("raw@laundryghar.local", user.Email);
        Assert.Equal("+911111111111", user.Phone);
    }

    // 3 ── the raw name wins when both are present, so behaviour is identical wherever it already
    //      worked; the mapped name is only ever a fallback.
    [Fact]
    public void the_raw_claim_takes_precedence_over_the_mapped_one()
    {
        var user = CurrentUser(
            new Claim("email", "raw@laundryghar.local"),
            new Claim(ClaimTypes.Email, "mapped@laundryghar.local"),
            new Claim("phone", "+912222222222"),
            new Claim(ClaimTypes.MobilePhone, "+913333333333"));

        Assert.Equal("raw@laundryghar.local", user.Email);
        Assert.Equal("+912222222222", user.Phone);
    }

    // 4 ── genuinely absent stays absent — the fallback must not invent a value. A user with no
    //      email on file should still get the honest "no email to verify against".
    [Fact]
    public void a_principal_with_neither_claim_reports_null()
    {
        var user = CurrentUser(new Claim("user_type", "staff"));

        Assert.Null(user.Email);
        Assert.Null(user.Phone);
    }

    // 5 ── HomePhone is accepted too: it is the other standard phone mapping, and which one a
    //      provider emits is not ours to control.
    [Fact]
    public void the_home_phone_mapping_is_also_accepted()
    {
        var user = CurrentUser(new Claim(ClaimTypes.HomePhone, "+914444444444"));
        Assert.Equal("+914444444444", user.Phone);
    }

    private static HttpContextCurrentUser CurrentUser(params Claim[] claims)
    {
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Bearer")),
        };
        return new HttpContextCurrentUser(new HttpContextAccessor { HttpContext = ctx });
    }
}

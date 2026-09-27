using System.Security.Claims;
using System.Text.Json;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Middlewares.ExceptionsMiddleware;
using laundryghar.Utilities.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace operations.Tests.Auth;

/// <summary>
/// Audit finding F-4 (docs/ABAC_AUDIT_2026-08-31.md): a request that does not name the brand it
/// acts on was answered with <b>401 Unauthorized</b>.
///
/// <para>The token is valid and the caller is authenticated; what is missing is a request
/// parameter. But 401 is the one status clients treat as "this session is dead":
/// <c>admin-web/src/api/client.ts</c> answers it by refreshing the token and, when the refresh
/// changes nothing, clearing auth and redirecting to <c>/login</c>. So a platform admin who had not
/// yet chosen a brand was logged out — and because the envelope flattens every
/// <see cref="UnauthorizedAccessException"/> to the single word "Unauthorized.", the handler's own
/// explanation never reached the client. Not even a developer reading the response could tell a
/// missing header from an expired token.</para>
///
/// <para>Measured live: 60 of the platform admin's 141 endpoint calls returned 401 for this reason
/// alone, and every one became a 200 once <c>X-Brand-Id</c> was supplied.</para>
///
/// <para>These tests pin both halves — that the throw is now a
/// <see cref="BrandContextRequiredException"/>, and that the middleware turns it into a 400
/// carrying a stable code — plus the negative: a genuine authentication failure must still be
/// 401, or the fix would have traded one wrong answer for another.</para>
/// </summary>
public class BrandContextRequiredTests
{
    // ── 1. The throw itself ────────────────────────────────────────────────────────────────────

    [Fact]
    public void a_caller_with_no_brand_gets_a_brand_context_error_not_an_auth_error()
    {
        var user = CurrentUser(new Claim("user_type", "platform_admin"));

        var ex = Assert.Throws<BrandContextRequiredException>(() => user.RequireBrandId());

        // The distinction the whole fix rests on: this must NOT be catchable as an auth failure,
        // because ExceptionHandler classifies that as 401.
        Assert.IsNotType<UnauthorizedAccessException>(ex);
        Assert.Contains("X-Brand-Id", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void the_brand_id_claim_still_resolves_unchanged()
    {
        var brand = Guid.NewGuid();
        var user = CurrentUser(new Claim("brand_id", brand.ToString()));

        Assert.Equal(brand, user.RequireBrandId());
    }

    [Fact]
    public void the_x_brand_id_override_still_resolves_unchanged()
    {
        // The header path: TenantResolutionMiddleware parks the resolved brand in Items, which is
        // how a platform admin adopts a tenant. This is the case whose ABSENCE produced the 401.
        var brand = Guid.NewGuid();
        var ctx = Context(new Claim("user_type", "platform_admin"));
        ctx.Items["brand_id_override"] = brand;

        var user = new HttpContextCurrentUser(new HttpContextAccessor { HttpContext = ctx });

        Assert.Equal(brand, user.RequireBrandId());
    }

    [Fact]
    public void an_empty_guid_brand_claim_is_treated_as_absent()
    {
        // Guid.Empty is not a brand. It used to fall through to the same 401; it must now fall
        // through to the same 400 rather than being accepted as a tenant.
        var user = CurrentUser(new Claim("brand_id", Guid.Empty.ToString()));

        Assert.Throws<BrandContextRequiredException>(() => user.RequireBrandId());
    }

    // ── 2. The wire response ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task the_middleware_answers_400_with_a_machine_readable_code()
    {
        var (status, body) = await RunAsync(new BrandContextRequiredException());

        Assert.Equal(StatusCodes.Status400BadRequest, status);

        var message = body.RootElement.GetProperty("message");
        Assert.Equal(400, message.GetProperty("errorTypeCode").GetInt32());

        // The code rides under "code", matching step_up_required / feature_not_in_plan, so a client
        // can branch on it and prompt for a brand instead of parsing prose.
        var code = message.GetProperty("errorMessage").GetProperty("code");
        Assert.Equal(
            BrandContextRequiredException.ErrorCode,
            code[0].GetString());
    }

    [Fact]
    public async Task the_explanation_survives_to_the_client()
    {
        // The old path discarded the handler's message and wrote the literal "Unauthorized.".
        var (_, body) = await RunAsync(
            new BrandContextRequiredException("Brand context required. Pass X-Brand-Id."));

        var responseMessage = body.RootElement
            .GetProperty("message").GetProperty("responseMessage").GetString();

        Assert.Equal("Brand context required. Pass X-Brand-Id.", responseMessage);
    }

    // ── 3. The negative: nothing else moved ────────────────────────────────────────────────────

    [Fact]
    public async Task a_real_authentication_failure_is_still_401()
    {
        // Guards the obvious over-correction. UnauthorizedAccessException must keep meaning
        // "your session is not good enough" — that is what the client's refresh-and-retry is for.
        var (status, body) = await RunAsync(new UnauthorizedAccessException("nope"));

        Assert.Equal(StatusCodes.Status401Unauthorized, status);
        Assert.Equal(
            "Unauthorized.",
            body.RootElement.GetProperty("message").GetProperty("responseMessage").GetString());
    }

    [Fact]
    public async Task a_forbidden_exception_is_still_403()
    {
        var (status, _) = await RunAsync(new ForbiddenException("no"));
        Assert.Equal(StatusCodes.Status403Forbidden, status);
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Drives the real middleware over a delegate that throws <paramref name="ex"/>.</summary>
    private static async Task<(int Status, JsonDocument Body)> RunAsync(Exception ex)
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();

        var handler = new ExceptionHandler(
            _ => throw ex,
            NullLogger<ExceptionHandler>.Instance);

        await handler.Invoke(ctx);

        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        return (ctx.Response.StatusCode, await JsonDocument.ParseAsync(ctx.Response.Body));
    }

    private static DefaultHttpContext Context(params Claim[] claims) =>
        new() { User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Bearer")) };

    private static HttpContextCurrentUser CurrentUser(params Claim[] claims) =>
        new(new HttpContextAccessor { HttpContext = Context(claims) });
}

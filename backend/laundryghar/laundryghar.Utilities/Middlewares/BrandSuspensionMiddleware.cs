using System.Text.Json;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace laundryghar.Utilities.Middlewares;

/// <summary>
/// §9 "Suspension = <b>login-only mode</b> (owner can see invoices and pay; operations frozen) —
/// never silent data loss."
///
/// <para>A suspended brand keeps every byte of its data and every one of its logins. What it loses is
/// the ability to <i>operate</i>: no new bookings, no dispatch, no catalogue edits. What it keeps is
/// exactly the path back — sign in, look at the invoice, pay it, and be reinstated. Cutting off the
/// billing screens along with everything else would strand the owner outside the only door that
/// reopens their account, which is why this is an allow-list rather than a blanket block.</para>
///
/// <para><b>Why a middleware and not a token claim.</b> Suspension must bite when the payment lapses,
/// not at the user's next login — a claim would let a brand trade for the remaining life of its
/// access token. The cost is a status lookup per request, bounded by <see cref="IBrandStatusStore"/>'s
/// cache.</para>
///
/// <para>Runs AFTER <c>TenantResolutionMiddleware</c> so the brand is resolved, and it never touches
/// anonymous traffic (nothing to suspend) or platform admins (we must still be able to operate on a
/// suspended tenant — including to reinstate it).</para>
/// </summary>
public sealed class BrandSuspensionMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The response code clients switch on. Distinct from <c>feature_not_in_plan</c>: both
    /// are 402, but the remediation differs — buy a feature vs settle an outstanding invoice.</summary>
    public const string ResponseMessage = "brand_suspended";

    /// <summary>The cancellation counterpart (§9 "export offered, wind-down retention, then
    /// deletion"). Same 402 and the same login-only shape, but a different remediation — withdraw
    /// the cancellation, or export before the window closes — so it gets its own code rather than
    /// being folded into <see cref="ResponseMessage"/> and mislabelled as a payment problem.</summary>
    public const string CancelledResponseMessage = "brand_cancelled";

    /// <summary>Path prefixes that stay open while suspended — the way back in, and nothing else.</summary>
    private static readonly string[] AllowedPrefixes =
    [
        "/api/v1/signup",                  // a brand-new provider has no brand to suspend
        "/api/v1/auth",                    // sign in, refresh, step-up
        "/api/v1/customer/auth",
        "/api/v1/partner/auth",
        "/oauth",
        "/api/v1/admin/entitlements",      // see the tier, the invoices, and pay them
        "/api/v1/webhooks",                // the gateway must be able to tell us the payment landed
        "/api/v1/admin/navigator",         // so the console renders a shell instead of a blank page
        "/api/v1/admin/cancellation",      // §9: export, and change your mind, during the wind-down
        "/health",
        "/.well-known",
    ];

    private readonly RequestDelegate _next;

    public BrandSuspensionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!ShouldCheck(context))
        {
            await _next(context);
            return;
        }

        var brandId = BrandIdOf(context);
        if (brandId is null)
        {
            await _next(context);
            return;
        }

        var store = context.RequestServices.GetService<IBrandStatusStore>();
        if (store is null)
        {
            await _next(context);
            return;
        }

        var status = await store.GetStatusAsync(brandId.Value, context.RequestAborted);

        // Two states are gated, both into the same login-only mode.
        //
        // 'cancelled' is a wind-down in progress (migration 0015). Freezing operations is the easy
        // half; the half that matters is that the allow-list above keeps EXPORT and WITHDRAW open.
        // A retention window the customer is locked out of is not a retention window — it is
        // deletion with a delay, and §8.2 promises the opposite: "take it with them if they leave."
        //
        // 'archived' is NOT gated here. Its data is already gone, there is nothing left to freeze,
        // and an unknown/null status fails OPEN — see IBrandStatusStore.
        var cancelled = string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase);
        if (!cancelled && !string.Equals(status, "suspended", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var code = cancelled ? CancelledResponseMessage : ResponseMessage;
        var explanation = cancelled
            ? "This account is being closed. You can still sign in to export your data, or withdraw "
              + "the cancellation, until the retention window ends."
            : "This account is suspended for non-payment. Settle the outstanding invoice to "
              + "restore access — your data is untouched.";

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
        context.Response.ContentType = "application/json";

        var payload = new Response
        {
            Status = false,
            Message = new Message
            {
                ErrorTypeCode   = ErrorMessageEnum.PaymentRequired,   // must match the 402 on the wire
                ResponseMessage = code,
                ErrorMessage    = new Dictionary<string, string[]> { [code] = [explanation] },
            },
        };

        await JsonSerializer.SerializeAsync(context.Response.Body, payload, JsonOptions);
    }

    private static bool ShouldCheck(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true) return false;

        // Platform operators keep working on a suspended tenant — that is how it gets reinstated.
        var userType = context.User.FindFirst("user_type")?.Value;
        if (userType == SharedDataModel.Enums.UserType.PlatformAdmin) return false;

        var path = context.Request.Path;
        foreach (var prefix in AllowedPrefixes)
            if (path.StartsWithSegments(prefix)) return false;

        return true;
    }

    /// <summary>The brand this request acts on: the platform-admin override if present, else the
    /// JWT's own brand_id.</summary>
    private static Guid? BrandIdOf(HttpContext context)
    {
        if (context.Items.TryGetValue("brand_id_override", out var o) && o is Guid overridden)
            return overridden;

        var raw = context.User.FindFirst("brand_id")?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}

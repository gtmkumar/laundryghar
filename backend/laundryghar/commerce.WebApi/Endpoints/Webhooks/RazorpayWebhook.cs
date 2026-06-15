using commerce.Application.Commerce.Webhooks;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Endpoints;

namespace commerce.WebApi.Endpoints.Webhooks;

/// <summary>
/// Anonymous Razorpay webhook receiver. POST /api/v1/webhooks/razorpay.
///
/// Security: AllowAnonymous — Razorpay signs each payload; the handler re-verifies
/// X-Razorpay-Signature = HMAC-SHA256(raw_body, brand WebhookSecret) (SEC-2) before acting.
/// The raw body bytes are read BEFORE model binding so the HMAC matches what Razorpay signed.
///
/// RLS: this unauthenticated path has no brand claim, so the handler must run with RLS
/// bypassed to find the payment by gateway_order_id. <c>ctx.Items["bypass_rls"]=true</c> is set
/// by the dedicated middleware in Program.cs BEFORE auth (so the RLS interceptor sees it when it
/// fixes the brand GUC at connection open); it is set again here defensively before dispatch.
/// </summary>
public class RazorpayWebhook : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/webhooks/razorpay";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Webhooks - Razorpay");
        group.MapPost(Receive, "/").AllowAnonymous();
    }

    public static async Task<IResult> Receive(HttpContext http, IDispatcher dispatcher, CancellationToken ct)
    {
        http.Request.EnableBuffering();
        using var ms = new MemoryStream();
        await http.Request.Body.CopyToAsync(ms, ct);
        var rawBody = ms.ToArray();

        var signature = http.Request.Headers["X-Razorpay-Signature"].FirstOrDefault();

        // Anonymous webhook → no brand claim. Bypass RLS so the handler can resolve the
        // payment by gateway_order_id (then re-scope the HMAC secret to that payment's brand).
        http.Items["bypass_rls"] = true;

        var result = await dispatcher.SendAsync(
            new ProcessRazorpayWebhookCommand(rawBody, signature), ct);

        return result.Accepted ? Results.Ok() : Results.BadRequest(result.Reason);
    }
}

using core.Application.Identity.Entitlements.Commands;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Endpoints;

namespace core.WebApi.Endpoints.Webhooks;

/// <summary>
/// Anonymous Razorpay Payment-Link webhook receiver (POST /api/v1/webhooks/razorpay-paylink). Marks the
/// matching brand-platform invoice paid on <c>payment_link.paid</c>. Signature re-verified in the handler
/// (HMAC-SHA256 with Razorpay:WebhookSecret); RLS bypassed (no brand claim) so the invoice is resolvable.
///
/// Distinct path from commerce's customer-payment webhook (/api/v1/webhooks/razorpay) — configure this
/// URL in the Razorpay dashboard for payment_link.* events.
/// </summary>
public class RazorpayPaylinkWebhook : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/webhooks/razorpay-paylink";

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

        // Anonymous → no brand claim. Bypass RLS so the handler can resolve the invoice by link id.
        http.Items["bypass_rls"] = true;

        var result = await dispatcher.SendAsync(new ProcessPaylinkWebhookCommand(rawBody, signature), ct);
        return result.Accepted ? Results.Ok() : Results.BadRequest(result.Reason);
    }
}

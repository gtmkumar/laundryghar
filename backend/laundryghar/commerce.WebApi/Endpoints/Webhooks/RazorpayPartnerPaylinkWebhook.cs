using commerce.Application.Commerce.Webhooks;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Endpoints;

namespace commerce.WebApi.Endpoints.Webhooks;

/// <summary>
/// Anonymous Razorpay Payment-Link webhook receiver for the RaaS partner-billing lane
/// (POST /api/v1/webhooks/razorpay-partner-paylink). On <c>payment_link.paid</c> it marks the matching
/// partner invoice paid or credits the partner wallet top-up (once). Signature is re-verified in the
/// handler (HMAC-SHA256 with the PLATFORM gateway webhook secret); RLS is bypassed (no partner claim)
/// so the invoice/wallet is resolvable.
///
/// Distinct paths, one dashboard config each:
///   • /api/v1/webhooks/razorpay                  — customer payments (payment.captured/failed).
///   • /api/v1/webhooks/razorpay-partner-paylink  — partner invoices + wallet top-ups (payment_link.*).
/// (core additionally serves /api/v1/webhooks/razorpay-paylink for brand SaaS invoices.)
/// </summary>
public class RazorpayPartnerPaylinkWebhook : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/webhooks/razorpay-partner-paylink";

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

        // Anonymous → no partner claim. Bypass RLS so the handler can resolve the partner invoice /
        // wallet. Set here defensively; the Program.cs pre-auth middleware sets it before the RLS
        // interceptor fixes the GUC at connection open.
        http.Items["bypass_rls"] = true;

        var result = await dispatcher.SendAsync(new ProcessPartnerPaylinkWebhookCommand(rawBody, signature), ct);
        return result.Accepted ? Results.Ok() : Results.BadRequest(result.Reason);
    }
}

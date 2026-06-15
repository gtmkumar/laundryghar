using commerce.Application.Commerce;
using commerce.Application.Commerce.Admin.Payments;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;

namespace commerce.WebApi.Endpoints.Commerce;

/// <summary>Admin — payments: read (permission:payment.read), record offline
/// (permission:payment.record), issue refund (permission:payment.refund), and the
/// config-driven webhook URL settings read (permission:paymentmethod.manage).</summary>
public class PaymentsAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/payments";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Commerce - Payments");
        group.RequireAuthorization();

        group.MapGet(GetAll, "/").RequireAuthorization("permission:payment.read");
        group.MapGet(GetById, "/{id:guid}").RequireAuthorization("permission:payment.read");
        group.MapPost(RecordOffline, "/")
            .AddEndpointFilter<ValidationFilter<RecordOfflinePaymentRequest>>()
            .RequireAuthorization("permission:payment.record");
        group.MapPost(IssueRefund, "/refunds").RequireAuthorization("permission:payment.refund");
        group.MapGet(GetSettings, "/settings").RequireAuthorization("permission:paymentmethod.manage");
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 20, Guid? customerId = null)
    {
        var r = await dispatcher.QueryAsync(new GetAdminPaymentsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, customerId), ct);
        return Results.Ok(new PaginatedListResponse<PaymentDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetAdminPaymentByIdQuery(id), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<PaymentDto> { Status = true, Data = r });
    }

    public static async Task<IResult> RecordOffline(
        HttpContext http, RecordOfflinePaymentRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        // H3b: honour Idempotency-Key header; fallback to body field then server-derived key.
        var idempotencyKey = http.Request.Headers.TryGetValue("Idempotency-Key", out var hdrKey)
            ? hdrKey.FirstOrDefault()
            : req.IdempotencyKey;

        var reqWithKey = req with { IdempotencyKey = idempotencyKey };
        var r = await dispatcher.SendAsync(new RecordOfflinePaymentCommand(reqWithKey, u.UserId), ct);
        return Results.Created($"/api/v1/admin/payments/{r.PaymentId}", new SingleResponse<OfflinePaymentDto> { Status = true, Data = r });
    }

    public static async Task<IResult> IssueRefund(IssueRefundRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new IssueRefundCommand(req, u.UserId), ct);
        return Results.Created($"/api/v1/admin/payments/refunds/{r.Id}", new SingleResponse<PaymentRefundDto> { Status = true, Data = r });
    }

    public static IResult GetSettings(IConfiguration config, ICurrentUser u)
    {
        // The canonical webhook URL is config-driven so it is correct in all environments
        // (dev, staging, prod) without the admin-web having to guess the service port.
        var baseUrl = config["PublicBaseUrls:Commerce"]?.TrimEnd('/') ?? "http://localhost:5002";
        var webhookUrl = $"{baseUrl}/api/v1/webhooks/razorpay";
        return Results.Ok(new SingleResponse<PaymentSettingsView>
        {
            Status = true,
            Data   = new PaymentSettingsView(webhookUrl)
        });
    }
}

/// <summary>Commerce payment settings read model returned to the admin panel.</summary>
public sealed record PaymentSettingsView(
    /// <summary>
    /// Canonical Razorpay webhook URL for this environment.
    /// Configure this URL in your Razorpay dashboard under Webhooks.
    /// Sourced from PublicBaseUrls:Commerce config key (default http://localhost:5002).
    /// </summary>
    string WebhookUrl
);

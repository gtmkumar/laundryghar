namespace core.Application.Common.Interfaces;

/// <summary>A created Razorpay Payment Link.</summary>
public sealed record RazorpayLink(string Id, string ShortUrl, string Status);

/// <summary>
/// Thin client over Razorpay Payment Links (https://razorpay.com/docs/payments/payment-links/) — the
/// B2B way to collect a brand's platform-tier invoice: create a hosted payable link, the brand pays it,
/// and the payment is reconciled (webhook or status sync). Credentials resolve settings-first: the
/// platform-scoped <c>payment/platform_gateway</c> row (Settings → Platform billing) when enabled, else
/// the env config Razorpay:KeyId / Razorpay:KeySecret.
/// </summary>
public interface IRazorpayLinkClient
{
    /// <summary>True only when a Razorpay KeyId + KeySecret resolve (from Settings → Platform billing or
    /// env config); else collection is unavailable. Async because resolution reads the settings store.</summary>
    Task<bool> IsConfiguredAsync(CancellationToken ct = default);

    /// <summary>Create a payment link for an amount (major units, e.g. rupees). <paramref name="referenceId"/>
    /// must be unique across links (we use the invoice id). <paramref name="notes"/> are echoed back on the
    /// payment + webhook for correlation.</summary>
    Task<RazorpayLink> CreatePaymentLinkAsync(
        decimal amount, string currency, string description, string referenceId,
        IReadOnlyDictionary<string, string>? notes = null, CancellationToken ct = default);

    /// <summary>Fetch a payment link's current status (created|paid|cancelled|expired).</summary>
    Task<string> GetPaymentLinkStatusAsync(string linkId, CancellationToken ct = default);
}

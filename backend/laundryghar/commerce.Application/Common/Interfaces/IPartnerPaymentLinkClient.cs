namespace commerce.Application.Common.Interfaces;

/// <summary>A created Razorpay Payment Link (partner-billing lane).</summary>
public sealed record PartnerPaymentLink(string Id, string ShortUrl, string Status);

/// <summary>The current state of a Razorpay Payment Link, pulled on demand.</summary>
/// <param name="Status">created | paid | cancelled | expired.</param>
/// <param name="AmountPaidMajor">Total collected on the link, in major units (rupees).</param>
/// <param name="Notes">The link's <c>notes</c>, echoed back verbatim by Razorpay. These were authored
/// server-side when the link was created (e.g. the wallet top-up sets <c>kind</c> / <c>partner_id</c> /
/// <c>idempotency_key</c>). Reconcilers MUST bind the credit to these server-set values, never to caller
/// input. Empty when the link carries no notes.</param>
public sealed record PartnerPaymentLinkDetails(
    string Status, decimal AmountPaidMajor, IReadOnlyDictionary<string, string> Notes);

/// <summary>
/// Thin client over Razorpay Payment Links for the RaaS partner-billing lane (partner invoices +
/// wallet top-ups). Mirrors core's <c>IRazorpayLinkClient</c> exactly — same Razorpay API, same
/// settings-first credential resolution — but lives in the commerce bounded context because commerce
/// cannot reference core.Infrastructure. Credentials resolve settings-first: the PLATFORM-scoped
/// <c>payment/platform_gateway</c> row (the operator's dedicated collection account) when enabled,
/// else env config <c>Razorpay:KeyId</c> / <c>Razorpay:KeySecret</c>. This is the same operator
/// account core uses to collect SaaS invoices — partner collection is the same B2B money-in flow.
/// </summary>
public interface IPartnerPaymentLinkClient
{
    /// <summary>True only when a Razorpay KeyId + KeySecret resolve (platform settings or env config).
    /// Async because resolution reads the settings store.</summary>
    Task<bool> IsConfiguredAsync(CancellationToken ct = default);

    /// <summary>Create a payment link for an amount (major units, e.g. rupees). <paramref name="referenceId"/>
    /// must be unique across links (we use the invoice/top-up id). <paramref name="notes"/> are echoed
    /// back on the payment + webhook for correlation.</summary>
    Task<PartnerPaymentLink> CreatePaymentLinkAsync(
        decimal amount, string currency, string description, string referenceId,
        IReadOnlyDictionary<string, string>? notes = null, CancellationToken ct = default);

    /// <summary>Fetch a payment link's current status + amount collected (for pull-based reconcile).</summary>
    Task<PartnerPaymentLinkDetails> GetPaymentLinkAsync(string linkId, CancellationToken ct = default);
}

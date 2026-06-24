namespace commerce.Infrastructure.Worker.Channels;

/// <summary>
/// Encodes the channel-preference ladder for lifecycle (utility) notifications.
/// Stateless — all inputs are booleans from the customer's opt-in flags.
///
/// Ladder (highest to lowest priority):
///   1. WhatsApp   — if WhatsappOptIn
///   2. SMS        — if SmsOptIn
///   3. Push       — if PushOptIn
///   4. null       — no channel available; caller logs suppression reason
///
/// WhatsApp template messages are UTILITY category (transactional).
/// All lifecycle events (status changes, payment, refund) flow through this ladder.
/// </summary>
public static class NotificationChannelPreferencePolicy
{
    /// <summary>
    /// Picks the best channel for a lifecycle notification given the customer's opt-ins.
    /// Returns the channel string matching the engagement_cms CHECK constraint values,
    /// or <c>null</c> if the customer has opted out of all supported channels.
    /// </summary>
    /// <param name="whatsappOptIn">Customer.WhatsappOptIn</param>
    /// <param name="smsOptIn">Customer.SmsOptIn</param>
    /// <param name="pushOptIn">Customer.PushOptIn</param>
    /// <returns>"whatsapp" | "sms" | "push" | null</returns>
    public static string? ResolveChannel(
        bool whatsappOptIn,
        bool smsOptIn,
        bool pushOptIn)
    {
        if (whatsappOptIn) return "whatsapp";
        if (smsOptIn)      return "sms";
        if (pushOptIn)     return "push";
        return null;
    }

    /// <summary>
    /// Maps an order lifecycle event type to the corresponding notification template code
    /// and a human-readable suppression reason when the customer has opted out.
    /// Returns null for event types that do not generate customer notifications.
    /// </summary>
    public static (string TemplateCode, string TemplateSuffix)? ResolveTemplate(
        string eventType,
        string? newStatus = null)
    {
        return (eventType, newStatus?.ToLowerInvariant()) switch
        {
            ("order.status_changed", "pickup_scheduled")    => ("ORDER_PICKUP_SCHEDULED", "_SMS"),
            ("order.status_changed", "picked_up")           => ("ORDER_PICKED_UP",        "_SMS"),
            ("order.status_changed", "ready")               => ("ORDER_READY",             "_WHATSAPP"),
            ("order.status_changed", "out_for_delivery")    => ("ORDER_OUT_FOR_DELIVERY",  "_SMS"),
            ("order.status_changed", "delivered")           => ("ORDER_DELIVERED",         "_SMS"),
            ("order.cancelled",      _)                     => ("ORDER_CANCELLED",         "_SMS"),
            ("payment.captured",     _)                     => ("PAYMENT_CAPTURED",        "_SMS"),
            ("refund.initiated",     _)                     => ("REFUND_INITIATED",        "_SMS"),
            // ── Warehouse lifecycle ─────────────────────────────────────────────
            ("fulfillment.lost",         _)                     => ("GARMENT_LOST",            "_SMS"),
            // ── Pickup lifecycle ────────────────────────────────────────────────
            ("pickup.rejected",      _)                     => ("PICKUP_REJECTED",          "_SMS"),
            _                                               => null
        };
    }

    /// <summary>
    /// Resolves the full template code to use given the preferred channel.
    /// E.g. "ORDER_READY" + channel "whatsapp" → "ORDER_READY_WHATSAPP".
    ///      "ORDER_READY" + channel "sms"       → "ORDER_READY_SMS".
    ///      "ORDER_READY" + channel "push"      → "ORDER_READY_PUSH".
    /// </summary>
    public static string BuildTemplateCode(string baseCode, string channel)
        => $"{baseCode}_{channel.ToUpperInvariant()}";
}

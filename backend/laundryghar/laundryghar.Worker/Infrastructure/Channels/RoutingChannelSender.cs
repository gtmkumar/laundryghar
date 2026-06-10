using laundryghar.Worker.Abstractions;
using laundryghar.Worker.Infrastructure.Stubs;

namespace laundryghar.Worker.Infrastructure.Channels;

/// <summary>
/// Composite <see cref="IChannelSender"/> that dispatches to the correct provider
/// based on the notification row's channel field.
///
/// Routing table (channel value → sender):
///   "whatsapp" → <see cref="WhatsAppCloudChannelSender"/> (or logging fallback)
///   "sms"      → <see cref="Msg91SmsChannelSender"/>      (or logging fallback)
///   "push"     → <see cref="ExpoPushChannelSender"/>       (or logging fallback)
///   anything else → logging fallback
///
/// Each sender is registered conditionally by <c>Program.cs</c>; if a provider
/// is not enabled/configured its service is null and the fallback kicks in.
/// This means zero configuration = log-only behavior in Development.
/// </summary>
internal sealed class RoutingChannelSender : IChannelSender
{
    private readonly WhatsAppCloudChannelSender? _whatsApp;
    private readonly Msg91SmsChannelSender?      _sms;
    private readonly ExpoPushChannelSender?      _push;
    private readonly LoggingChannelSender        _fallback;
    private readonly ILogger<RoutingChannelSender> _logger;

    public RoutingChannelSender(
        LoggingChannelSender             fallback,
        ILogger<RoutingChannelSender>    logger,
        WhatsAppCloudChannelSender?      whatsApp = null,
        Msg91SmsChannelSender?           sms      = null,
        ExpoPushChannelSender?           push     = null)
    {
        _whatsApp = whatsApp;
        _sms      = sms;
        _push     = push;
        _fallback = fallback;
        _logger   = logger;
    }

    public Task<ChannelSendResult> SendAsync(ChannelSendRequest request, CancellationToken ct = default)
    {
        IChannelSender sender = request.Channel.ToLowerInvariant() switch
        {
            "whatsapp" when _whatsApp is not null => _whatsApp,
            "sms"      when _sms      is not null => _sms,
            "push"     when _push     is not null => _push,
            _                                     => _fallback
        };

        if (sender == _fallback && request.Channel is not ("in_app" or "email" or "voice"))
        {
            _logger.LogDebug(
                "[Router] No real provider configured for channel={Channel}; using logging stub. OutboxId={OutboxId}",
                request.Channel, request.OutboxId);
        }

        return sender.SendAsync(request, ct);
    }
}

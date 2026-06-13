using laundryghar.SharedDataModel.Persistence;
using laundryghar.Worker.Abstractions;
using laundryghar.Worker.Infrastructure.Stubs;
using laundryghar.Worker.Options;
using Microsoft.Extensions.Options;

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
/// Settings-first resolution (WhatsApp + SMS only):
///   If <c>kernel.system_settings</c> has an enabled row with credentials
///   (TTL-cached via <see cref="NotificationSettingsCache"/>), those credentials
///   override the env-config options. Falls back to env config when no DB row
///   exists or it is disabled. Falls back to <see cref="LoggingChannelSender"/>
///   when neither source provides credentials — existing zero-config dev behaviour
///   is fully preserved.
/// </summary>
internal sealed class RoutingChannelSender : IChannelSender
{
    private readonly NotificationSettingsCache?  _settingsCache;
    private readonly LaundryGharDbContext        _db;
    private readonly IOptions<WhatsAppOptions>   _whatsAppEnvOpts;
    private readonly IOptions<SmsOptions>        _smsEnvOpts;
    private readonly WhatsAppCloudChannelSender? _whatsApp;
    private readonly Msg91SmsChannelSender?      _sms;
    private readonly ExpoPushChannelSender?      _push;
    private readonly LoggingChannelSender        _fallback;
    private readonly IHttpClientFactory          _httpFactory;
    private readonly ILogger<RoutingChannelSender> _logger;

    public RoutingChannelSender(
        LaundryGharDbContext             db,
        IOptions<WhatsAppOptions>        whatsAppEnvOpts,
        IOptions<SmsOptions>             smsEnvOpts,
        IHttpClientFactory               httpFactory,
        LoggingChannelSender             fallback,
        ILogger<RoutingChannelSender>    logger,
        WhatsAppCloudChannelSender?      whatsApp       = null,
        Msg91SmsChannelSender?           sms            = null,
        ExpoPushChannelSender?           push           = null,
        NotificationSettingsCache?       settingsCache  = null)
    {
        _db              = db;
        _whatsAppEnvOpts = whatsAppEnvOpts;
        _smsEnvOpts      = smsEnvOpts;
        _httpFactory     = httpFactory;
        _whatsApp        = whatsApp;
        _sms             = sms;
        _push            = push;
        _fallback        = fallback;
        _logger          = logger;
        _settingsCache   = settingsCache;
    }

    public async Task<ChannelSendResult> SendAsync(ChannelSendRequest request, CancellationToken ct = default)
    {
        var channel = request.Channel.ToLowerInvariant();

        if (channel == "whatsapp")
        {
            var sender = await ResolveWhatsAppAsync(ct);
            return await sender.SendAsync(request, ct);
        }

        if (channel == "sms")
        {
            var sender = await ResolveSmsAsync(ct);
            return await sender.SendAsync(request, ct);
        }

        IChannelSender routedSender = channel switch
        {
            "push" when _push is not null => _push,
            _                             => _fallback
        };

        if (routedSender == _fallback && channel is not ("in_app" or "email" or "voice"))
        {
            _logger.LogDebug(
                "[Router] No real provider configured for channel={Channel}; using logging stub. OutboxId={OutboxId}",
                channel, request.OutboxId);
        }

        return await routedSender.SendAsync(request, ct);
    }

    // ── Settings-first resolution ─────────────────────────────────────────────

    private async Task<IChannelSender> ResolveWhatsAppAsync(CancellationToken ct)
    {
        if (_settingsCache is not null)
        {
            var (wa, _) = await _settingsCache.GetAsync(_db, ct);
            if (wa.Enabled
                && !string.IsNullOrWhiteSpace(wa.AccessToken)
                && !string.IsNullOrWhiteSpace(wa.PhoneNumberId))
            {
                _logger.LogDebug("[Router] WhatsApp: resolved from DB settings.");
                var opts = new WhatsAppOptions
                {
                    Enabled       = true,
                    AccessToken   = wa.AccessToken,
                    PhoneNumberId = wa.PhoneNumberId,
                };
                return new WhatsAppCloudChannelSender(
                    _httpFactory,
                    Microsoft.Extensions.Options.Options.Create(opts),
                    _logger as ILogger<WhatsAppCloudChannelSender>
                        ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<WhatsAppCloudChannelSender>.Instance);
            }
        }

        // Env config fallback (registered sender from Program.cs)
        if (_whatsApp is not null)
        {
            _logger.LogDebug("[Router] WhatsApp: resolved from env config.");
            return _whatsApp;
        }

        _logger.LogDebug("[Router] WhatsApp: no credentials — using logging stub.");
        return _fallback;
    }

    private async Task<IChannelSender> ResolveSmsAsync(CancellationToken ct)
    {
        if (_settingsCache is not null)
        {
            var (_, sms) = await _settingsCache.GetAsync(_db, ct);
            if (sms.Enabled
                && !string.IsNullOrWhiteSpace(sms.AuthKey)
                && !string.IsNullOrWhiteSpace(sms.SenderId))
            {
                _logger.LogDebug("[Router] SMS: resolved from DB settings.");
                var opts = new SmsOptions
                {
                    Enabled       = true,
                    AuthKey       = sms.AuthKey,
                    SenderId      = sms.SenderId,
                    DltTemplateId = sms.DltTemplateId,
                };
                return new Msg91SmsChannelSender(
                    _httpFactory,
                    Microsoft.Extensions.Options.Options.Create(opts),
                    _logger as ILogger<Msg91SmsChannelSender>
                        ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<Msg91SmsChannelSender>.Instance);
            }
        }

        // Env config fallback
        if (_sms is not null)
        {
            _logger.LogDebug("[Router] SMS: resolved from env config.");
            return _sms;
        }

        _logger.LogDebug("[Router] SMS: no credentials — using logging stub.");
        return _fallback;
    }
}

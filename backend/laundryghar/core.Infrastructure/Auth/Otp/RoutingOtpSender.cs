using core.Application.Common.Interfaces;
using core.Application.Identity.Settings;
using laundryghar.SharedDataModel.Crypto;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace core.Infrastructure.Auth;

/// <summary>
/// Channel-routing OTP sender: WhatsApp authentication template first (when
/// the admin has enabled OTP-over-WhatsApp), MSG91 SMS as fallback, dev-log
/// as the last resort in Development only.
///
/// Channel config lives in kernel.system_settings (the admin Settings page),
/// loaded fresh per send so admin changes apply without a restart. A channel
/// failure is logged and the next channel is tried; only when every channel
/// in the plan fails (or none is configured) does the send throw — login is
/// too critical for a silent drop.
/// </summary>
public sealed class RoutingOtpSender : IOtpSender
{
    private readonly ICoreDbContext         _db;
    private readonly IFieldCipher           _cipher;
    private readonly WhatsAppOtpDispatcher  _whatsApp;
    private readonly Msg91OtpDispatcher     _sms;
    private readonly DevLogOtpSender        _devLog;
    private readonly IHostEnvironment       _env;
    private readonly ILogger<RoutingOtpSender> _logger;

    public RoutingOtpSender(
        ICoreDbContext db,
        IFieldCipher cipher,
        WhatsAppOtpDispatcher whatsApp,
        Msg91OtpDispatcher sms,
        DevLogOtpSender devLog,
        IHostEnvironment env,
        ILogger<RoutingOtpSender> logger)
    {
        _db       = db;
        _cipher   = cipher;
        _whatsApp = whatsApp;
        _sms      = sms;
        _devLog   = devLog;
        _env      = env;
        _logger   = logger;
    }

    public async Task SendAsync(
        string identifier, string identifierType,
        string plainCode, string purpose,
        CancellationToken ct = default, Guid? brandId = null)
    {
        // brandId: non-null for customer OTP (brand-scoped settings preferred);
        // null for staff/pre-auth paths where no brand context is available.
        // FindAsync prefers a brand-scoped row over the platform row when brandId is non-null.
        var whatsApp = await SettingsStore.LoadWhatsAppAsync(_db, brandId: brandId, _cipher, ct);
        var sms      = await SettingsStore.LoadSmsAsync(_db, brandId: brandId, _cipher, ct);

        var plan = OtpChannelPlanner.Plan(whatsApp, sms, identifierType, _env.IsDevelopment());
        if (plan.Count == 0)
            throw new InvalidOperationException(
                "No OTP delivery channel is configured. Enable WhatsApp OTP or SMS (MSG91) " +
                "in admin Settings before going live.");

        List<Exception>? failures = null;
        foreach (var channel in plan)
        {
            try
            {
                switch (channel)
                {
                    case OtpChannel.WhatsApp:
                        await _whatsApp.SendAsync(whatsApp, identifier, plainCode, ct);
                        return;
                    case OtpChannel.Sms:
                        await _sms.SendAsync(sms, identifier, plainCode, ct);
                        return;
                    case OtpChannel.DevLog:
                        await _devLog.SendAsync(identifier, identifierType, plainCode, purpose, ct);
                        return;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                (failures ??= []).Add(ex);
                _logger.LogWarning(ex,
                    "[OTP] {Channel} delivery failed for {IdentifierType}; trying next channel.",
                    channel, identifierType);
            }
        }

        throw new InvalidOperationException(
            $"OTP delivery failed on all configured channels ({string.Join(", ", plan)}).",
            failures is { Count: > 0 } ? new AggregateException(failures) : null);
    }
}

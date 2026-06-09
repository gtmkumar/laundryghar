using laundryghar.Identity.Application.Settings.Dtos;
using laundryghar.Identity.Infrastructure.Services;
using laundryghar.SharedDataModel.Persistence;
using MediatR;

namespace laundryghar.Identity.Application.Settings.Queries;

public sealed record GetAdminSettingsQuery(ICurrentUser User) : IRequest<AdminSettingsView>;

public sealed class GetAdminSettingsHandler : IRequestHandler<GetAdminSettingsQuery, AdminSettingsView>
{
    private readonly LaundryGharDbContext _db;
    public GetAdminSettingsHandler(LaundryGharDbContext db) => _db = db;

    public async Task<AdminSettingsView> Handle(GetAdminSettingsQuery q, CancellationToken ct)
    {
        var brandId = await SettingsStore.ResolveBrandIdAsync(q.User, _db, ct);

        var email = await SettingsStore.LoadEmailAsync(_db, brandId, ct);
        var mode = await SettingsStore.LoadProvisioningModeAsync(_db, brandId, ct);
        var baseUrl = await SettingsStore.LoadAdminBaseUrlAsync(_db, brandId, ct);
        var maps = await SettingsStore.LoadMapsAsync(_db, brandId, ct);
        var payout = await SettingsStore.LoadPayoutAsync(_db, brandId, ct);

        return new AdminSettingsView(
            new EmailSettingsView(
                email.Enabled, email.Host, email.Port, email.Secure,
                email.Username, PasswordSet: !string.IsNullOrEmpty(email.Password),
                email.FromEmail, email.FromName),
            new ProvisioningView(mode),
            new AppUrlsView(baseUrl),
            new MapsSettingsView(maps.Provider, maps.GoogleApiKey, maps.MapboxToken),
            new PayoutSettingsView(payout.BaseFare, payout.PerKm, payout.ExpressBonus, payout.CodBonus, payout.RoundToNearest));
    }
}

using System.Text.Json;
using laundryghar.Identity.Infrastructure.Auth;
using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Persistence;
using laundryghar.Utilities.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace laundryghar.Identity.Application.Settings.Commands;

// ── Fare settings (brand-scoped) ─────────────────────────────────────────────

public sealed record GetFareSettingsQuery(ICurrentUser User) : IRequest<FareSettings>;
public sealed record UpdateFareSettingsCommand(FareSettings Request, ICurrentUser User) : IRequest<FareSettings>;

public sealed class GetFareSettingsHandler : IRequestHandler<GetFareSettingsQuery, FareSettings>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly LaundryGharDbContext _db;
    public GetFareSettingsHandler(LaundryGharDbContext db) => _db = db;

    public async Task<FareSettings> Handle(GetFareSettingsQuery q, CancellationToken ct)
    {
        var brandId = await SettingsStore.ResolveBrandIdAsync(q.User, _db, ct);
        var row = await SettingsStore.FindAsync(_db, brandId, "fare", "quote", ct);
        if (row is null) return new FareSettings();
        try { return JsonSerializer.Deserialize<FareSettings>(row.SettingValue, Json) ?? new FareSettings(); }
        catch (JsonException) { return new FareSettings(); }
    }
}

public sealed class UpdateFareSettingsHandler : IRequestHandler<UpdateFareSettingsCommand, FareSettings>
{
    private readonly LaundryGharDbContext _db;
    public UpdateFareSettingsHandler(LaundryGharDbContext db) => _db = db;

    public async Task<FareSettings> Handle(UpdateFareSettingsCommand cmd, CancellationToken ct)
    {
        var r = cmd.Request;
        var errors = new Dictionary<string, string[]>();
        if (r.MinFare < 0) errors["minFare"] = ["Min fare cannot be negative."];
        if (r.RoundToNearest <= 0) errors["roundToNearest"] = ["Round-to must be greater than zero."];
        if (r.QuoteTtlSeconds <= 0) errors["quoteTtlSeconds"] = ["Quote TTL must be greater than zero."];
        foreach (var (tier, rate) in r.TierRates)
            if (rate.BaseFare < 0 || rate.PerKm < 0 || rate.PickupFlat < 0)
                errors[$"tierRates.{tier}"] = ["Tier rates cannot be negative."];
        foreach (var w in r.Surge)
            if (w.Multiplier < 1m) errors["surge"] = ["Surge multiplier must be at least 1."];
        if (errors.Count > 0) throw new ValidationException(errors);

        var brandId = await SettingsStore.ResolveBrandIdAsync(cmd.User, _db, ct);
        await SettingsStore.UpsertAsync(_db, brandId, "fare", "quote", r, isEncrypted: false, cmd.User.UserId, ct);
        return r;
    }
}

// ── Dispatch mode (platform-scoped; offer_accept gated by dispatch.mode.manage) ──

public sealed record GetDispatchSettingsQuery(ICurrentUser User) : IRequest<DispatchSettings>;
public sealed record UpdateDispatchSettingsCommand(DispatchSettings Request, ICurrentUser User) : IRequest<DispatchSettings>;

public sealed class GetDispatchSettingsHandler : IRequestHandler<GetDispatchSettingsQuery, DispatchSettings>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly LaundryGharDbContext _db;
    public GetDispatchSettingsHandler(LaundryGharDbContext db) => _db = db;

    public async Task<DispatchSettings> Handle(GetDispatchSettingsQuery q, CancellationToken ct)
    {
        // The mode lives at platform scope (brand rows may only narrow to push).
        var row = await SettingsStore.FindAsync(_db, brandId: null, "dispatch", "mode", ct);
        if (row is null) return new DispatchSettings();
        try { return JsonSerializer.Deserialize<DispatchSettings>(row.SettingValue, Json) ?? new DispatchSettings(); }
        catch (JsonException) { return new DispatchSettings(); }
    }
}

public sealed class UpdateDispatchSettingsHandler : IRequestHandler<UpdateDispatchSettingsCommand, DispatchSettings>
{
    private readonly LaundryGharDbContext _db;
    public UpdateDispatchSettingsHandler(LaundryGharDbContext db) => _db = db;

    public async Task<DispatchSettings> Handle(UpdateDispatchSettingsCommand cmd, CancellationToken ct)
    {
        var r = cmd.Request;

        // Enabling offer_accept is a platform-level decision behind dispatch.mode.manage.
        if (r.Mode == DispatchSettings.ModeOfferAccept && !cmd.User.HasPermission("dispatch.mode.manage"))
            throw new BusinessRuleException("Enabling offer→accept dispatch requires the dispatch.mode.manage permission.");

        var errors = new Dictionary<string, string[]>();
        if (r.Mode is not (DispatchSettings.ModePush or DispatchSettings.ModeOfferAccept))
            errors["mode"] = ["Mode must be push or offer_accept."];
        if (r.OfferTtlSeconds <= 0) errors["offerTtlSeconds"] = ["Offer TTL must be greater than zero."];
        if (r.MaxOfferRounds <= 0) errors["maxOfferRounds"] = ["Max offer rounds must be greater than zero."];
        if (r.OffersPerRound <= 0) errors["offersPerRound"] = ["Offers per round must be greater than zero."];
        if (errors.Count > 0) throw new ValidationException(errors);

        // Platform scope (brandId = null).
        await SettingsStore.UpsertAsync(_db, brandId: null, "dispatch", "mode", r, isEncrypted: false, cmd.User.UserId, ct);
        return r;
    }
}

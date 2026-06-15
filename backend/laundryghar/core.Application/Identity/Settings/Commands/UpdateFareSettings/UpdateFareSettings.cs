using core.Application.Common.Interfaces;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Common;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;

namespace core.Application.Identity.Settings.Commands.UpdateFareSettings;

public sealed record UpdateFareSettingsCommand(FareSettings Request) : ICommand<FareSettings>;

public sealed class UpdateFareSettingsHandler : ICommandHandler<UpdateFareSettingsCommand, FareSettings>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateFareSettingsHandler(ICoreDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<FareSettings> HandleAsync(UpdateFareSettingsCommand command, CancellationToken ct)
    {
        var r = command.Request;
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

        var brandId = await SettingsStore.ResolveBrandIdAsync(_user, _db, ct);
        await SettingsStore.UpsertAsync(_db, brandId, "fare", "quote", r, isEncrypted: false, _user.UserId, ct);
        return r;
    }
}

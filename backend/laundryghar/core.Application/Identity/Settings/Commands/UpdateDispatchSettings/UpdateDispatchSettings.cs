using core.Application.Common.Interfaces;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Common;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;

namespace core.Application.Identity.Settings.Commands.UpdateDispatchSettings;

public sealed record UpdateDispatchSettingsCommand(DispatchSettings Request) : ICommand<DispatchSettings>;

public sealed class UpdateDispatchSettingsHandler : ICommandHandler<UpdateDispatchSettingsCommand, DispatchSettings>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateDispatchSettingsHandler(ICoreDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<DispatchSettings> HandleAsync(UpdateDispatchSettingsCommand command, CancellationToken ct)
    {
        var r = command.Request;

        // Enabling offer_accept is a platform-level decision behind dispatch.mode.manage.
        if (r.Mode == DispatchSettings.ModeOfferAccept && !_user.HasPermission("dispatch.mode.manage"))
            throw new BusinessRuleException("Enabling offer→accept dispatch requires the dispatch.mode.manage permission.");

        var errors = new Dictionary<string, string[]>();
        if (r.Mode is not (DispatchSettings.ModePush or DispatchSettings.ModeOfferAccept))
            errors["mode"] = ["Mode must be push or offer_accept."];
        if (r.OfferTtlSeconds <= 0) errors["offerTtlSeconds"] = ["Offer TTL must be greater than zero."];
        if (r.MaxOfferRounds <= 0) errors["maxOfferRounds"] = ["Max offer rounds must be greater than zero."];
        if (r.OffersPerRound <= 0) errors["offersPerRound"] = ["Offers per round must be greater than zero."];
        if (errors.Count > 0) throw new ValidationException(errors);

        // Platform scope (brandId = null).
        await SettingsStore.UpsertAsync(_db, brandId: null, "dispatch", "mode", r, isEncrypted: false, _user.UserId, ct);
        return r;
    }
}

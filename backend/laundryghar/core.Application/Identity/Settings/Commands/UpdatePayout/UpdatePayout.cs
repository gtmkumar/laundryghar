using core.Application.Common.Interfaces;
using core.Application.Identity.Settings.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Common;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;

namespace core.Application.Identity.Settings.Commands.UpdatePayout;

public sealed record UpdatePayoutCommand(UpdatePayoutSettingsRequest Request) : ICommand<PayoutSettingsView>;

public sealed class UpdatePayoutHandler : ICommandHandler<UpdatePayoutCommand, PayoutSettingsView>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;

    public UpdatePayoutHandler(ICoreDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<PayoutSettingsView> HandleAsync(UpdatePayoutCommand command, CancellationToken ct)
    {
        var r = command.Request;
        var errors = new Dictionary<string, string[]>();
        if (r.BaseFare < 0)     errors["baseFare"]     = ["Base fare cannot be negative."];
        if (r.PerKm < 0)        errors["perKm"]        = ["Per-km rate cannot be negative."];
        if (r.ExpressBonus < 0) errors["expressBonus"] = ["Express bonus cannot be negative."];
        if (r.CodBonus < 0)     errors["codBonus"]     = ["COD bonus cannot be negative."];
        if (r.RoundToNearest <= 0) errors["roundToNearest"] = ["Round-to must be greater than zero."];
        if (errors.Count > 0) throw new ValidationException(errors);

        var brandId = await SettingsStore.ResolveBrandIdAsync(_user, _db, ct);
        var value = new RiderPayoutSettings
        {
            BaseFare = r.BaseFare, PerKm = r.PerKm, ExpressBonus = r.ExpressBonus,
            CodBonus = r.CodBonus, RoundToNearest = r.RoundToNearest,
        };
        await SettingsStore.UpsertAsync(_db, brandId, "payout", "rider", value, isEncrypted: false, _user.UserId, ct);
        return new PayoutSettingsView(value.BaseFare, value.PerKm, value.ExpressBonus, value.CodBonus, value.RoundToNearest);
    }
}

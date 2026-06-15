using core.Application.Common.Interfaces;
using core.Application.Identity.Settings.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;

namespace core.Application.Identity.Settings.Commands.UpdateProvisioning;

public sealed record UpdateProvisioningCommand(UpdateProvisioningRequest Request) : ICommand<ProvisioningView>;

public sealed class UpdateProvisioningHandler : ICommandHandler<UpdateProvisioningCommand, ProvisioningView>
{
    private static readonly HashSet<string> Modes = new(StringComparer.OrdinalIgnoreCase) { "admin_activate", "self_service" };
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateProvisioningHandler(ICoreDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<ProvisioningView> HandleAsync(UpdateProvisioningCommand command, CancellationToken ct)
    {
        var mode = command.Request.Mode?.Trim().ToLowerInvariant() ?? "";
        if (!Modes.Contains(mode))
            throw new ValidationException(new Dictionary<string, string[]>
                { ["mode"] = ["Mode must be 'admin_activate' or 'self_service'."] });

        var brandId = await SettingsStore.ResolveBrandIdAsync(_user, _db, ct);
        await SettingsStore.UpsertAsync(_db, brandId, "provisioning", "invite", new { mode }, isEncrypted: false, _user.UserId, ct);
        return new ProvisioningView(mode);
    }
}

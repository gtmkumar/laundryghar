using core.Application.Common.Interfaces;
using core.Application.Identity.Users.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;

namespace core.Application.Identity.Users.Commands.SetUserType;

public sealed record SetUserTypeCommand(Guid UserId, SetUserTypeRequest Request) : ICommand<bool>;

/// <summary>
/// H3: Changes a user's type. Only callable by actors whose own type is at the same
/// level or higher than the requested type (prevent self-elevation).
/// Requires permission users.set_type.
/// </summary>
public class SetUserTypeCommandHandler : ICommandHandler<SetUserTypeCommand, bool>
{
    // Priority: lower value = higher privilege. Mirrors the seeded roles.Priority ordering.
    private static readonly Dictionary<string, int> TypePriority = new(StringComparer.OrdinalIgnoreCase)
    {
        [laundryghar.SharedDataModel.Enums.UserType.PlatformAdmin]     = 10,
        [laundryghar.SharedDataModel.Enums.UserType.BrandAdmin]        = 20,
        [laundryghar.SharedDataModel.Enums.UserType.FranchiseOwner]    = 40,
        [laundryghar.SharedDataModel.Enums.UserType.StoreAdmin]        = 50,
        [laundryghar.SharedDataModel.Enums.UserType.Staff]             = 60,
        [laundryghar.SharedDataModel.Enums.UserType.WarehouseStaff]    = 80,
        // Vertical-neutral on-site processing staff — same privilege tier as the laundry
        // warehouse_staff it succeeds. Without this entry GetValueOrDefault() returned
        // int.MaxValue, silently skipping the privilege-escalation guard for ops_staff.
        [laundryghar.SharedDataModel.Enums.UserType.OpsStaff]          = 80,
        [laundryghar.SharedDataModel.Enums.UserType.Rider]             = 90,
        [laundryghar.SharedDataModel.Enums.UserType.Auditor]           = 100,
        [laundryghar.SharedDataModel.Enums.UserType.Support]           = 110,
    };

    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _actor;
    public SetUserTypeCommandHandler(ICoreDbContext db, ICurrentUser actor) { _db = db; _actor = actor; }

    public async Task<bool> HandleAsync(SetUserTypeCommand cmd, CancellationToken ct)
    {
        var actor = _actor;

        // Only platform_admin can assign platform_admin type
        if (cmd.Request.NewUserType == laundryghar.SharedDataModel.Enums.UserType.PlatformAdmin
            && actor.UserType != laundryghar.SharedDataModel.Enums.UserType.PlatformAdmin)
        {
            throw new UnauthorizedAccessException(
                "Only a platform_admin may assign the platform_admin user type.");
        }

        // Actor's own type must have equal or higher privilege than the type being assigned
        var actorPriority  = TypePriority.GetValueOrDefault(actor.UserType ?? string.Empty, int.MaxValue);
        var targetPriority = TypePriority.GetValueOrDefault(cmd.Request.NewUserType, int.MaxValue);

        if (targetPriority < actorPriority)
        {
            throw new UnauthorizedAccessException(
                "You cannot assign a user type with higher privileges than your own.");
        }

        var user = await _db.Users.FindAsync([cmd.UserId], ct);
        if (user is null) return false;

        user.UserType  = cmd.Request.NewUserType;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        user.UpdatedBy = actor.UserId;
        user.Version++;

        await _db.SaveChangesAsync(ct);
        return true;
    }
}

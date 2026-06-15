using core.Application.Common.Interfaces;
using core.Application.Identity.AccessControl.Commands.InviteUser;
using core.Application.Identity.AccessControl.Dtos;
using core.Application.Identity.Users.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.AccessControl.Commands.InviteRider;

// ── Invite a rider (franchise-scoped, requires rider.manage) ────────────────
public sealed record InviteRiderCommand(InviteRiderRequest Request) : ICommand<UserDto>;

public class InviteRiderCommandHandler : ICommandHandler<InviteRiderCommand, UserDto>
{
    private readonly IDispatcher _dispatcher;
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _actor;

    public InviteRiderCommandHandler(IDispatcher dispatcher, ICoreDbContext db, ICurrentUser actor)
    { _dispatcher = dispatcher; _db = db; _actor = actor; }

    public async Task<UserDto> HandleAsync(InviteRiderCommand cmd, CancellationToken ct)
    {
        var r = cmd.Request;

        if (string.IsNullOrWhiteSpace(r.Email))
            throw new ValidationException(
                new Dictionary<string, string[]> { ["email"] = ["Email is required."] });

        // ── Resolve the seeded rider role ────────────────────────────────────
        var riderRoleId = await _db.Roles
            .Where(ro => ro.Code == "rider" && ro.DeletedAt == null)
            .Select(ro => (Guid?)ro.Id)
            .FirstOrDefaultAsync(ct)
            ?? throw new ValidationException(
                new Dictionary<string, string[]> { ["role"] = ["The seeded 'rider' role was not found. Contact a platform administrator."] });

        // ── Franchise scoping ────────────────────────────────────────────────
        // Franchise-scoped actors are locked to their own franchise; brand/platform
        // admins supply the franchiseId in the request but we validate brand ownership.
        Guid franchiseId;

        if (_actor.FranchiseId is Guid actorFid)
        {
            // Franchise owner / franchise staff: force their own franchise regardless of request.
            franchiseId = actorFid;
        }
        else
        {
            // Brand admin or platform admin: use the request value, validate it belongs to their brand.
            if (r.FranchiseId == Guid.Empty)
                throw new ValidationException(
                    new Dictionary<string, string[]> { ["franchiseId"] = ["FranchiseId is required."] });

            franchiseId = r.FranchiseId;

            if (!_actor.IsPlatformAdmin)
            {
                var brandId = _actor.BrandId
                    ?? throw new ValidationException(
                        new Dictionary<string, string[]> { ["actor"] = ["Could not determine your brand context. Re-authenticate and try again."] });

                var franchiseBelongsToBrand = await _db.Franchises
                    .AnyAsync(f => f.Id == franchiseId && f.BrandId == brandId && f.DeletedAt == null, ct);

                if (!franchiseBelongsToBrand)
                    throw new ValidationException(
                        new Dictionary<string, string[]> { ["franchiseId"] = ["Franchise not found or does not belong to your brand."] });
            }
        }

        // ── Delegate to the shared InviteUserCommand (creates user + membership + email) ──
        return await _dispatcher.SendAsync(new InviteUserCommand(
            new InviteUserRequest(
                Email:     r.Email,
                Phone:     r.Phone,
                FirstName: r.FirstName,
                LastName:  r.LastName,
                UserType:  "rider",
                RoleId:    riderRoleId,
                ScopeType: "franchise",
                ScopeId:   franchiseId,
                Password:  null)), ct);
    }
}

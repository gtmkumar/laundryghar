using System.Security.Cryptography;
using core.Application.Common.Interfaces;
using core.Application.Identity.AccessControl.Commands.GrantMembership;
using core.Application.Identity.Onboarding.Dtos;
using core.Application.Identity.Onboarding.Queries.GetOnboardingState;
using core.Application.Identity.Users.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Onboarding.Commands.InviteOwner;

// Step 3: franchise owner (invite a new user or link an existing one) and grant the
// primary franchise_owner membership for this franchise.
//
// RECONCILED (AdminUser migration): the membership grant now dispatches GrantMembershipCommand
// (AccessControl slice) so the privilege-escalation guards (rank / brand-scope / platform-admin)
// run consistently with every other grant path — previously the membership was inlined here and
// skipped those guards. The create-or-link + OwnerUserId behaviour is intentionally KEPT inline:
// InviteUserCommand always creates a NEW user (and emails) and cannot link an existing owner by
// email, which is the distinct contract this onboarding step needs.
public sealed record InviteOwnerCommand(Guid FranchiseId, InviteOwnerRequest Request) : ICommand<OnboardingStateDto?>;

public class InviteOwnerCommandHandler : ICommandHandler<InviteOwnerCommand, OnboardingStateDto?>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _actor;
    private readonly IDispatcher _dispatcher;

    public InviteOwnerCommandHandler(ICoreDbContext db, ICurrentUser actor, IDispatcher dispatcher)
    { _db = db; _actor = actor; _dispatcher = dispatcher; }

    public async Task<OnboardingStateDto?> HandleAsync(InviteOwnerCommand command, CancellationToken cancellationToken)
    {
        var f = await _db.Franchises.FirstOrDefaultAsync(x => x.Id == command.FranchiseId && x.DeletedAt == null, cancellationToken);
        if (f is null) return null;
        var r = command.Request;
        if (string.IsNullOrWhiteSpace(r.Email))
            throw new ValidationException(new Dictionary<string, string[]> { ["email"] = ["Owner email is required."] });

        var roleId = await _db.Roles.AsNoTracking().Where(x => x.Code == "franchise_owner" && x.DeletedAt == null)
            .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(cancellationToken)
            ?? throw new ValidationException(new Dictionary<string, string[]> { ["role"] = ["franchise_owner role is missing."] });

        var email = r.Email.Trim();
        var ownerId = await _db.Users.AsNoTracking()
            .Where(u => u.Email == email && u.Status != UserStatus.Deleted)
            .Select(u => u.Id).FirstOrDefaultAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;

        if (ownerId == Guid.Empty)
        {
            // Create the owner as an invited (token-based, password-less) user + profile.
            ownerId = Guid.NewGuid();
            _db.Users.Add(new User
            {
                Id = ownerId,
                Email = email,
                PhoneE164 = string.IsNullOrWhiteSpace(r.Phone) ? null : r.Phone,
                UserType = UserType.FranchiseOwner,
                Status = UserStatus.Invited,
                InvitationToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                InvitationSentAt = now,
                Locale = "en-IN", Timezone = "Asia/Kolkata", FailedAttempts = 0,
                CreatedAt = now, UpdatedAt = now, CreatedBy = _actor.UserId, Version = 1
            });

            if (r.FirstName is not null || r.LastName is not null)
            {
                _db.UserProfiles.Add(new UserProfile
                {
                    UserId = ownerId, FirstName = r.FirstName, LastName = r.LastName,
                    Preferences = "{}", Metadata = "{}", Status = "active",
                    CreatedAt = now, UpdatedAt = now, CreatedBy = _actor.UserId
                });
            }

            // Persist the new user before granting — GrantMembershipCommand operates on a
            // separately-tracked entity graph and the FK must exist.
            await _db.SaveChangesAsync(cancellationToken);
        }

        // Grant the primary franchise_owner membership for this franchise. Dispatched through
        // GrantMembershipCommand so the privilege-escalation guards are enforced; it also
        // demotes any existing primary membership the user holds (replace semantics).
        await _dispatcher.SendAsync(new GrantMembershipCommand(
            new GrantMembershipRequest(ownerId, ScopeType.Franchise, f.Id, roleId, IsPrimary: true),
            _actor.UserId), cancellationToken);

        f.OwnerUserId = ownerId;
        f.UpdatedAt = now; f.UpdatedBy = _actor.UserId; f.Version++;
        await _db.SaveChangesAsync(cancellationToken);
        return await OnboardingState.BuildAsync(_db, f, cancellationToken);
    }
}

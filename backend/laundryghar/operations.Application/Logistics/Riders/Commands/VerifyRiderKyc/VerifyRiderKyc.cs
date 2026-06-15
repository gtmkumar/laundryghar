using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.Riders.Commands.CreateRider;
using operations.Application.Logistics.Riders.Dtos;

namespace operations.Application.Logistics.Riders.Commands.VerifyRiderKyc;

// ── Verify Rider KYC ─────────────────────────────────────────────────────────

public sealed record VerifyRiderKycCommand(Guid Id, Guid? ActorId) : ICommand<RiderDto?>;

public sealed class VerifyRiderKycHandler : ICommandHandler<VerifyRiderKycCommand, RiderDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public VerifyRiderKycHandler(IOperationsDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<RiderDto?> HandleAsync(VerifyRiderKycCommand command, CancellationToken cancellationToken)
    {
        var ct      = cancellationToken;
        var brandId = _user.RequireBrandId();
        var rider   = await _db.Riders
            .FirstOrDefaultAsync(r => r.Id == command.Id && r.BrandId == brandId, ct);
        if (rider is null) return null;

        // Franchise scoping (defense-in-depth): franchise-scoped actors must not
        // verify riders that belong to a different franchise.
        if (_user.FranchiseId is Guid fid && rider.FranchiseId != fid) return null;

        // Idempotent: already verified — return current state without mutation.
        if (rider.KycStatus == RiderKycStatus.Verified)
        {
            var dtoCurrent = await CreateRiderHandler.LoadEnrichedAsync(_db, rider, ct);
            return RiderDtoFinancialMask.Apply(dtoCurrent, _user);
        }

        var now = DateTimeOffset.UtcNow;
        rider.KycStatus    = RiderKycStatus.Verified;
        rider.KycVerifiedAt = now;
        rider.OnboardedAt  ??= now;
        rider.UpdatedAt    = now;
        rider.UpdatedBy    = command.ActorId;

        // Activate the linked login (invited → active only).
        // User lifecycle normally lives in the Identity service, but KYC approval
        // is the explicit gate that allows a rider to log in — activating the login
        // here is intentional and by design. Only the invited→active transition is
        // applied; active/suspended/locked users are left untouched.
        var linkedUser = await _db.Users.FirstOrDefaultAsync(u => u.Id == rider.UserId, ct);
        if (linkedUser?.Status == UserStatus.Invited)
        {
            linkedUser.Status    = UserStatus.Active;
            linkedUser.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(ct);
        var dtoVerified = await CreateRiderHandler.LoadEnrichedAsync(_db, rider, ct);
        return RiderDtoFinancialMask.Apply(dtoVerified, _user);
    }
}

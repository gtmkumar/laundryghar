using System.Text.Json.Nodes;
using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.Riders.Commands.CreateRider;
using operations.Application.Logistics.Riders.Dtos;

namespace operations.Application.Logistics.Riders.Commands.RejectRiderKyc;

// ── Reject Rider KYC ─────────────────────────────────────────────────────────

public sealed record RejectRiderKycCommand(Guid Id, string? Reason, Guid? ActorId) : ICommand<RiderDto?>;

public sealed class RejectRiderKycHandler : ICommandHandler<RejectRiderKycCommand, RiderDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public RejectRiderKycHandler(IOperationsDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<RiderDto?> HandleAsync(RejectRiderKycCommand command, CancellationToken cancellationToken)
    {
        var ct      = cancellationToken;
        var brandId = _user.RequireBrandId();
        var rider   = await _db.Riders
            .FirstOrDefaultAsync(r => r.Id == command.Id && r.BrandId == brandId, ct);
        if (rider is null) return null;

        // Franchise scoping (defense-in-depth): franchise-scoped actors must not
        // reject riders that belong to a different franchise.
        if (_user.FranchiseId is Guid fid && rider.FranchiseId != fid) return null;

        var now = DateTimeOffset.UtcNow;
        rider.KycStatus = RiderKycStatus.Rejected;
        rider.UpdatedAt = now;
        rider.UpdatedBy = command.ActorId;

        // Merge kycRejectionReason into existing Metadata JSON without clobbering
        // other keys. Parse the existing object (defaulting to {}) then set the key.
        if (!string.IsNullOrWhiteSpace(command.Reason))
        {
            var meta   = JsonNode.Parse(rider.Metadata ?? "{}") as JsonObject ?? new JsonObject();
            meta["kycRejectionReason"] = JsonValue.Create(command.Reason.Trim());
            rider.Metadata = meta.ToJsonString();
        }

        await _db.SaveChangesAsync(ct);
        var dtoRejected = await CreateRiderHandler.LoadEnrichedAsync(_db, rider, ct);
        return RiderDtoFinancialMask.Apply(dtoRejected, _user);
    }
}

public sealed class RejectRiderRequestValidator : AbstractValidator<RejectRiderRequest>
{
    public RejectRiderRequestValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(1000).When(x => x.Reason is not null);
    }
}

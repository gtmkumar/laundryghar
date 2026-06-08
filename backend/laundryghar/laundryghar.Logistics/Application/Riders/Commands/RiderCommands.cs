using System.Text.Json.Nodes;
using FluentValidation;
using laundryghar.Logistics.Application.Riders.Dtos;
using laundryghar.SharedDataModel.Enums;
using MediatR;

namespace laundryghar.Logistics.Application.Riders.Commands;

// ── Create Rider ──────────────────────────────────────────────────────────────

public sealed record CreateRiderCommand(CreateRiderRequest Request, Guid? ActorId)
    : IRequest<RiderDto>;

public sealed class CreateRiderHandler : IRequestHandler<CreateRiderCommand, RiderDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public CreateRiderHandler(LaundryGharDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<RiderDto> Handle(CreateRiderCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req     = cmd.Request;
        var now     = DateTimeOffset.UtcNow;

        // Franchise scoping (defense-in-depth): if the actor is franchise-scoped,
        // force the new rider into their own franchise regardless of what was sent.
        if (_user.FranchiseId is Guid actorFid)
            req = req with { FranchiseId = actorFid };

        // Verify the referenced user exists and is of user_type='rider'
        var userExists = await _db.Users
            .AnyAsync(u => u.Id == req.UserId
                        && u.UserType == UserType.Rider
                        && u.DeletedAt == null, ct);
        if (!userExists)
            throw new BusinessRuleException("UserId must reference an active user with user_type='rider'.");

        // The users table is global (no RLS), so confirm this user actually belongs
        // to the current brand via a live membership before binding a profile to it —
        // otherwise an operator could attach another brand's rider and leak their PII.
        var userInBrand = await _db.UserScopeMemberships.AnyAsync(m =>
            m.UserId == req.UserId && m.RevokedAt == null
            && ((m.ScopeType == "brand"     && m.ScopeId == brandId)
             || (m.ScopeType == "franchise" && _db.Franchises.Any(f => f.Id == m.ScopeId && f.BrandId == brandId))
             || (m.ScopeType == "store"     && _db.Stores.Any(s => s.Id == m.ScopeId && s.BrandId == brandId))), ct);
        if (!userInBrand)
            throw new BusinessRuleException("UserId does not belong to the current brand.");

        // Franchise (and optional primary store) must belong to the current brand.
        var franchiseInBrand = await _db.Franchises.AnyAsync(f => f.Id == req.FranchiseId && f.BrandId == brandId, ct);
        if (!franchiseInBrand)
            throw new BusinessRuleException("FranchiseId does not belong to the current brand.");
        if (req.PrimaryStoreId is Guid storeId)
        {
            var storeInBrand = await _db.Stores.AnyAsync(s => s.Id == storeId && s.BrandId == brandId && s.FranchiseId == req.FranchiseId, ct);
            if (!storeInBrand)
                throw new BusinessRuleException("PrimaryStoreId must be a store of the selected franchise in the current brand.");
        }

        // Prevent duplicate rider profile for same user under same brand
        var duplicate = await _db.Riders.IgnoreQueryFilters()
            .AnyAsync(r => r.UserId == req.UserId && r.BrandId == brandId, ct);
        if (duplicate)
            throw new BusinessRuleException("A rider profile already exists for this user under the current brand.");

        // Auto-generate rider code: R-YYYYMMDD-NNNN
        var count = await _db.Riders.IgnoreQueryFilters().CountAsync(r => r.BrandId == brandId, ct);
        var riderCode = $"R-{now:yyyyMMdd}-{(count + 1):D4}";

        var rider = new Rider
        {
            Id                   = Guid.NewGuid(),
            UserId               = req.UserId,
            BrandId              = brandId,
            FranchiseId          = req.FranchiseId,
            PrimaryStoreId       = req.PrimaryStoreId,
            RiderCode            = riderCode,
            EmploymentType       = req.EmploymentType,
            VehicleType          = req.VehicleType,
            VehicleNumber        = req.VehicleNumber,
            VehicleModel         = req.VehicleModel,
            DrivingLicenseNumber = req.DrivingLicenseNumber,
            DlExpiryDate         = req.DlExpiryDate,
            AadhaarNumberMasked  = req.AadhaarNumberMasked,
            PanNumber            = req.PanNumber,
            InsuranceExpiryDate  = req.InsuranceExpiryDate,
            BankAccountNumber    = req.BankAccountNumber,
            BankIfsc             = req.BankIfsc,
            BankAccountName      = req.BankAccountName,
            UpiId                = req.UpiId,
            DailyPickupCapacity  = req.DailyPickupCapacity,
            DailyDeliveryCapacity = req.DailyDeliveryCapacity,
            ServiceRadiusKm      = req.ServiceRadiusKm,
            RatingAverage        = null,
            RatingCount          = 0,
            CompletionRate       = null,
            LifetimeDeliveries   = 0,
            IsOnline             = false,
            IsOnDuty             = false,
            CurrentLoad          = 0,
            KycStatus            = RiderKycStatus.Pending,
            Status               = RiderStatus.Active,
            Metadata             = "{}",
            CreatedAt            = now,
            UpdatedAt            = now,
            CreatedBy            = cmd.ActorId,
            UpdatedBy            = cmd.ActorId
        };

        _db.Riders.Add(rider);
        await _db.SaveChangesAsync(ct);
        return await LoadEnrichedAsync(_db, rider, ct);
    }

    /// <summary>
    /// Builds a <see cref="RiderDto"/> from a fully-populated <see cref="Rider"/> entity
    /// plus the pre-fetched identity/org context. Used by query projections.
    /// </summary>
    internal static RiderDto ToDto(
        Rider   r,
        string? riderName,
        string? email,
        string? phone,
        string? userStatus,
        string? franchiseName,
        string? primaryStoreName) => new(
            r.Id,               r.UserId,           r.BrandId,
            r.FranchiseId,      r.PrimaryStoreId,   r.RiderCode,
            r.EmploymentType,   r.VehicleType,
            r.VehicleNumber,    r.VehicleModel,      r.DrivingLicenseNumber,
            r.DlExpiryDate,     r.InsuranceExpiryDate,
            r.DailyPickupCapacity, r.DailyDeliveryCapacity, r.ServiceRadiusKm,
            r.RatingAverage,    r.RatingCount,
            r.CompletionRate,   r.LifetimeDeliveries,
            r.IsOnline,         r.IsOnDuty,         r.CurrentLoad,
            r.KycStatus,        r.Status,
            r.CreatedAt,        r.UpdatedAt,
            riderName,          email,              phone,
            userStatus,         franchiseName,      primaryStoreName);

    /// <summary>
    /// Re-queries the enrichment rows for a single rider after a write operation
    /// (create / update / deactivate) to avoid leaving the new fields null.
    /// Executes three lightweight point-lookups; not called on list paths.
    /// </summary>
    internal static async Task<RiderDto> LoadEnrichedAsync(
        LaundryGharDbContext db, Rider r, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == r.UserId)
            .Select(u => new { u.Email, u.PhoneE164, u.Status })
            .FirstOrDefaultAsync(ct);

        var rawName = await db.UserProfiles.AsNoTracking()
            .Where(p => p.UserId == r.UserId)
            .Select(p => ((p.FirstName ?? "") + " " + (p.LastName ?? "")).Trim())
            .FirstOrDefaultAsync(ct);
        var riderName = string.IsNullOrWhiteSpace(rawName) ? null : rawName;

        var franchise = await db.Franchises.AsNoTracking()
            .Where(f => f.Id == r.FranchiseId)
            .Select(f => f.DisplayName ?? f.LegalName)
            .FirstOrDefaultAsync(ct);

        string? storeName = r.PrimaryStoreId.HasValue
            ? await db.Stores.AsNoTracking()
                .Where(s => s.Id == r.PrimaryStoreId.Value)
                .Select(s => s.Name)
                .FirstOrDefaultAsync(ct)
            : null;

        return ToDto(r, riderName, user?.Email, user?.PhoneE164, user?.Status, franchise, storeName);
    }
}

// ── Update Rider ──────────────────────────────────────────────────────────────

public sealed record UpdateRiderCommand(Guid Id, UpdateRiderRequest Request, Guid? ActorId)
    : IRequest<RiderDto?>;

public sealed class UpdateRiderHandler : IRequestHandler<UpdateRiderCommand, RiderDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateRiderHandler(LaundryGharDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<RiderDto?> Handle(UpdateRiderCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var rider   = await _db.Riders
            .FirstOrDefaultAsync(r => r.Id == cmd.Id && r.BrandId == brandId, ct);
        if (rider is null) return null;

        // Franchise scoping (defense-in-depth): franchise-scoped actors must not
        // modify riders that belong to a different franchise.
        if (_user.FranchiseId is Guid fid && rider.FranchiseId != fid) return null;

        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        if (req.Status             is not null) rider.Status               = req.Status;
        if (req.VehicleNumber      is not null) rider.VehicleNumber        = req.VehicleNumber;
        if (req.VehicleModel       is not null) rider.VehicleModel         = req.VehicleModel;
        if (req.DrivingLicenseNumber is not null) rider.DrivingLicenseNumber = req.DrivingLicenseNumber;
        if (req.DlExpiryDate       is not null) rider.DlExpiryDate        = req.DlExpiryDate;
        if (req.InsuranceExpiryDate is not null) rider.InsuranceExpiryDate = req.InsuranceExpiryDate;
        if (req.DailyPickupCapacity is not null) rider.DailyPickupCapacity = req.DailyPickupCapacity.Value;
        if (req.DailyDeliveryCapacity is not null) rider.DailyDeliveryCapacity = req.DailyDeliveryCapacity.Value;
        if (req.ServiceRadiusKm    is not null) rider.ServiceRadiusKm     = req.ServiceRadiusKm.Value;
        if (req.PrimaryStoreId     is not null) rider.PrimaryStoreId      = req.PrimaryStoreId;

        rider.UpdatedAt = now;
        rider.UpdatedBy = cmd.ActorId;

        await _db.SaveChangesAsync(ct);
        return await CreateRiderHandler.LoadEnrichedAsync(_db, rider, ct);
    }
}

// ── Deactivate Rider ──────────────────────────────────────────────────────────

public sealed record DeactivateRiderCommand(Guid Id, Guid? ActorId) : IRequest<RiderDto?>;

public sealed class DeactivateRiderHandler : IRequestHandler<DeactivateRiderCommand, RiderDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public DeactivateRiderHandler(LaundryGharDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<RiderDto?> Handle(DeactivateRiderCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var rider   = await _db.Riders
            .FirstOrDefaultAsync(r => r.Id == cmd.Id && r.BrandId == brandId, ct);
        if (rider is null) return null;

        // Franchise scoping (defense-in-depth): franchise-scoped actors must not
        // deactivate riders that belong to a different franchise.
        if (_user.FranchiseId is Guid fid && rider.FranchiseId != fid) return null;

        var now = DateTimeOffset.UtcNow;
        rider.Status    = RiderStatus.Terminated;
        rider.DeletedAt = now;
        rider.UpdatedAt = now;
        rider.UpdatedBy = cmd.ActorId;

        await _db.SaveChangesAsync(ct);
        return await CreateRiderHandler.LoadEnrichedAsync(_db, rider, ct);
    }
}

// ── Verify Rider KYC ─────────────────────────────────────────────────────────

public sealed record VerifyRiderKycCommand(Guid Id, Guid? ActorId) : IRequest<RiderDto?>;

public sealed class VerifyRiderKycHandler : IRequestHandler<VerifyRiderKycCommand, RiderDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public VerifyRiderKycHandler(LaundryGharDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<RiderDto?> Handle(VerifyRiderKycCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var rider   = await _db.Riders
            .FirstOrDefaultAsync(r => r.Id == cmd.Id && r.BrandId == brandId, ct);
        if (rider is null) return null;

        // Franchise scoping (defense-in-depth): franchise-scoped actors must not
        // verify riders that belong to a different franchise.
        if (_user.FranchiseId is Guid fid && rider.FranchiseId != fid) return null;

        // Idempotent: already verified — return current state without mutation.
        if (rider.KycStatus == RiderKycStatus.Verified)
            return await CreateRiderHandler.LoadEnrichedAsync(_db, rider, ct);

        var now = DateTimeOffset.UtcNow;
        rider.KycStatus    = RiderKycStatus.Verified;
        rider.KycVerifiedAt = now;
        rider.OnboardedAt  ??= now;
        rider.UpdatedAt    = now;
        rider.UpdatedBy    = cmd.ActorId;

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
        return await CreateRiderHandler.LoadEnrichedAsync(_db, rider, ct);
    }
}

// ── Reject Rider KYC ─────────────────────────────────────────────────────────

public sealed record RejectRiderKycCommand(Guid Id, string? Reason, Guid? ActorId) : IRequest<RiderDto?>;

public sealed class RejectRiderKycHandler : IRequestHandler<RejectRiderKycCommand, RiderDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public RejectRiderKycHandler(LaundryGharDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<RiderDto?> Handle(RejectRiderKycCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var rider   = await _db.Riders
            .FirstOrDefaultAsync(r => r.Id == cmd.Id && r.BrandId == brandId, ct);
        if (rider is null) return null;

        // Franchise scoping (defense-in-depth): franchise-scoped actors must not
        // reject riders that belong to a different franchise.
        if (_user.FranchiseId is Guid fid && rider.FranchiseId != fid) return null;

        var now = DateTimeOffset.UtcNow;
        rider.KycStatus = RiderKycStatus.Rejected;
        rider.UpdatedAt = now;
        rider.UpdatedBy = cmd.ActorId;

        // Merge kycRejectionReason into existing Metadata JSON without clobbering
        // other keys. Parse the existing object (defaulting to {}) then set the key.
        if (!string.IsNullOrWhiteSpace(cmd.Reason))
        {
            var meta   = JsonNode.Parse(rider.Metadata ?? "{}") as JsonObject ?? new JsonObject();
            meta["kycRejectionReason"] = JsonValue.Create(cmd.Reason.Trim());
            rider.Metadata = meta.ToJsonString();
        }

        await _db.SaveChangesAsync(ct);
        return await CreateRiderHandler.LoadEnrichedAsync(_db, rider, ct);
    }
}

// ── Validators ────────────────────────────────────────────────────────────────

public sealed class CreateRiderValidator : AbstractValidator<CreateRiderCommand>
{
    public CreateRiderValidator()
    {
        RuleFor(x => x.Request.UserId).NotEmpty();
        RuleFor(x => x.Request.FranchiseId).NotEmpty();
        RuleFor(x => x.Request.EmploymentType)
            .Must(t => new[] { RiderEmploymentType.Employee, RiderEmploymentType.Contractor, RiderEmploymentType.Gig, RiderEmploymentType.Outsourced }.Contains(t))
            .WithMessage("Invalid employment_type.");
        RuleFor(x => x.Request.VehicleType)
            .Must(t => new[] { RiderVehicleType.TwoWheeler, RiderVehicleType.ThreeWheeler, RiderVehicleType.FourWheeler, RiderVehicleType.Cycle, RiderVehicleType.Foot }.Contains(t))
            .WithMessage("Invalid vehicle_type.");
        RuleFor(x => x.Request.DailyPickupCapacity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request.DailyDeliveryCapacity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request.ServiceRadiusKm).GreaterThan(0);
    }
}

public sealed class UpdateRiderValidator : AbstractValidator<UpdateRiderCommand>
{
    public UpdateRiderValidator()
    {
        // Status is the only privileged free-string field still settable here; whitelist it.
        // (KYC status is no longer assignable — it flows through verify/reject only.)
        RuleFor(x => x.Request.Status)
            .Must(s => new[] { RiderStatus.Active, RiderStatus.Suspended, RiderStatus.Terminated }.Contains(s))
            .When(x => x.Request.Status is not null)
            .WithMessage("Invalid status.");
        RuleFor(x => x.Request.DailyPickupCapacity).GreaterThanOrEqualTo(0).When(x => x.Request.DailyPickupCapacity is not null);
        RuleFor(x => x.Request.DailyDeliveryCapacity).GreaterThanOrEqualTo(0).When(x => x.Request.DailyDeliveryCapacity is not null);
        RuleFor(x => x.Request.ServiceRadiusKm).GreaterThan(0).When(x => x.Request.ServiceRadiusKm is not null);
    }
}

public sealed class RejectRiderValidator : AbstractValidator<RejectRiderKycCommand>
{
    public RejectRiderValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(1000).When(x => x.Reason is not null);
    }
}

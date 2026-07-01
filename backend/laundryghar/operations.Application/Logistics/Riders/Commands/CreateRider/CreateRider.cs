using System.Text.Json.Nodes;
using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.Logistics;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.Riders.Dtos;

namespace operations.Application.Logistics.Riders.Commands.CreateRider;

// ── Create Rider ──────────────────────────────────────────────────────────────

public sealed record CreateRiderCommand(CreateRiderRequest Request, Guid? ActorId)
    : ICommand<RiderDto>;

public sealed class CreateRiderHandler : ICommandHandler<CreateRiderCommand, RiderDto>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public CreateRiderHandler(IOperationsDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<RiderDto> HandleAsync(CreateRiderCommand command, CancellationToken cancellationToken)
    {
        var ct      = cancellationToken;
        var brandId = _user.RequireBrandId();
        var req     = command.Request;
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

        // RBAC sub-brand scope guard: the franchise/store the rider is being created
        // under come from the request, so confirm they fall within the caller's assigned scope.
        if (!_user.IsWithinScope(brandId: brandId, franchiseId: req.FranchiseId, storeId: req.PrimaryStoreId))
            throw new ForbiddenException("This rider is outside your assigned scope.");

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
            CreatedBy            = command.ActorId,
            UpdatedBy            = command.ActorId
        };

        _db.Riders.Add(rider);
        await _db.SaveChangesAsync(ct);
        var dto = await LoadEnrichedAsync(_db, rider, ct);
        return RiderDtoFinancialMask.Apply(dto, _user);
    }

    /// <summary>
    /// Builds a <see cref="RiderDto"/> from a fully-populated <see cref="Rider"/> entity
    /// plus the pre-fetched identity/org context. Used by query projections.
    /// PII fields are included in the DTO; callers must apply
    /// <see cref="RiderDtoFinancialMask.Apply"/> before returning to the HTTP layer.
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
            userStatus,         franchiseName,      primaryStoreName,
            // Financial PII — populated here; masking applied by the handler before HTTP response.
            r.PanNumber,        r.BankAccountNumber,
            r.BankIfsc,         r.BankAccountName,  r.UpiId,
            r.VehicleVerificationStatus);

    /// <summary>
    /// Re-queries the enrichment rows for a single rider after a write operation
    /// (create / update / deactivate) to avoid leaving the new fields null.
    /// Executes three lightweight point-lookups; not called on list paths.
    /// </summary>
    internal static async Task<RiderDto> LoadEnrichedAsync(
        IOperationsDbContext db, Rider r, CancellationToken ct)
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

public sealed class CreateRiderRequestValidator : AbstractValidator<CreateRiderRequest>
{
    public CreateRiderRequestValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.FranchiseId).NotEmpty();
        RuleFor(x => x.EmploymentType)
            .Must(t => new[] { RiderEmploymentType.Employee, RiderEmploymentType.Contractor, RiderEmploymentType.Gig, RiderEmploymentType.Outsourced }.Contains(t))
            .WithMessage("Invalid employment_type.");
        RuleFor(x => x.VehicleType)
            .Must(t => new[] { RiderVehicleType.TwoWheeler, RiderVehicleType.ThreeWheeler, RiderVehicleType.FourWheeler, RiderVehicleType.Cycle, RiderVehicleType.Foot }.Contains(t))
            .WithMessage("Invalid vehicle_type.");
        RuleFor(x => x.DailyPickupCapacity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DailyDeliveryCapacity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ServiceRadiusKm).GreaterThan(0);
    }
}

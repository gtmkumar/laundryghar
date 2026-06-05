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

        // Verify the referenced user exists and is of user_type='rider'
        var userExists = await _db.Users
            .AnyAsync(u => u.Id == req.UserId
                        && u.UserType == UserType.Rider
                        && u.DeletedAt == null, ct);
        if (!userExists)
            throw new BusinessRuleException("UserId must reference an active user with user_type='rider'.");

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
        return ToDto(rider);
    }

    internal static RiderDto ToDto(Rider r) => new(
        r.Id, r.UserId, r.BrandId, r.FranchiseId, r.PrimaryStoreId,
        r.RiderCode, r.EmploymentType, r.VehicleType,
        r.VehicleNumber, r.VehicleModel, r.DrivingLicenseNumber,
        r.DlExpiryDate, r.InsuranceExpiryDate,
        r.DailyPickupCapacity, r.DailyDeliveryCapacity,
        r.ServiceRadiusKm, r.RatingAverage, r.RatingCount,
        r.CompletionRate, r.LifetimeDeliveries,
        r.IsOnline, r.IsOnDuty, r.CurrentLoad,
        r.KycStatus, r.Status, r.CreatedAt, r.UpdatedAt);
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
        if (req.KycStatus          is not null) rider.KycStatus           = req.KycStatus;
        if (req.PrimaryStoreId     is not null) rider.PrimaryStoreId      = req.PrimaryStoreId;

        rider.UpdatedAt = now;
        rider.UpdatedBy = cmd.ActorId;

        await _db.SaveChangesAsync(ct);
        return CreateRiderHandler.ToDto(rider);
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

        var now = DateTimeOffset.UtcNow;
        rider.Status    = RiderStatus.Terminated;
        rider.DeletedAt = now;
        rider.UpdatedAt = now;
        rider.UpdatedBy = cmd.ActorId;

        await _db.SaveChangesAsync(ct);
        return CreateRiderHandler.ToDto(rider);
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

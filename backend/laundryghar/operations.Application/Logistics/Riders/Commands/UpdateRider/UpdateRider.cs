using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.Riders.Commands.CreateRider;
using operations.Application.Logistics.Riders.Dtos;

namespace operations.Application.Logistics.Riders.Commands.UpdateRider;

// ── Update Rider ──────────────────────────────────────────────────────────────

public sealed record UpdateRiderCommand(Guid Id, UpdateRiderRequest Request, Guid? ActorId)
    : ICommand<RiderDto?>;

public sealed class UpdateRiderHandler : ICommandHandler<UpdateRiderCommand, RiderDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateRiderHandler(IOperationsDbContext db, ICurrentUser user)
    { _db = db; _user = user; }

    public async Task<RiderDto?> HandleAsync(UpdateRiderCommand command, CancellationToken cancellationToken)
    {
        var ct      = cancellationToken;
        var brandId = _user.RequireBrandId();
        var rider   = await _db.Riders
            .FirstOrDefaultAsync(r => r.Id == command.Id && r.BrandId == brandId, ct);
        if (rider is null) return null;

        // Franchise scoping (defense-in-depth): franchise-scoped actors must not
        // modify riders that belong to a different franchise.
        if (_user.FranchiseId is Guid fid && rider.FranchiseId != fid) return null;

        var req = command.Request;
        var now = DateTimeOffset.UtcNow;

        // Cross-brand guard: a new primary store must be a store of the rider's
        // franchise in the current brand.
        if (req.PrimaryStoreId is Guid newStoreId)
        {
            var storeInBrand = await _db.Stores.AnyAsync(
                s => s.Id == newStoreId && s.BrandId == brandId && s.FranchiseId == rider.FranchiseId, ct);
            if (!storeInBrand)
                throw new BusinessRuleException("PrimaryStoreId must be a store of the rider's franchise in the current brand.");
        }

        if (req.Status             is not null) rider.Status               = req.Status;
        if (req.EmploymentType     is not null) rider.EmploymentType       = req.EmploymentType;
        if (req.VehicleType        is not null) rider.VehicleType          = req.VehicleType;
        if (req.VehicleNumber      is not null) rider.VehicleNumber        = req.VehicleNumber;
        if (req.VehicleModel       is not null) rider.VehicleModel         = req.VehicleModel;
        if (req.DrivingLicenseNumber is not null) rider.DrivingLicenseNumber = req.DrivingLicenseNumber;
        if (req.DlExpiryDate       is not null) rider.DlExpiryDate        = req.DlExpiryDate;
        if (req.AadhaarNumberMasked is not null) rider.AadhaarNumberMasked = req.AadhaarNumberMasked;
        if (req.PanNumber          is not null) rider.PanNumber            = req.PanNumber;
        if (req.InsuranceExpiryDate is not null) rider.InsuranceExpiryDate = req.InsuranceExpiryDate;
        if (req.BankAccountNumber  is not null) rider.BankAccountNumber    = req.BankAccountNumber;
        if (req.BankIfsc           is not null) rider.BankIfsc             = req.BankIfsc;
        if (req.BankAccountName    is not null) rider.BankAccountName      = req.BankAccountName;
        if (req.UpiId              is not null) rider.UpiId                = req.UpiId;
        if (req.DailyPickupCapacity is not null) rider.DailyPickupCapacity = req.DailyPickupCapacity.Value;
        if (req.DailyDeliveryCapacity is not null) rider.DailyDeliveryCapacity = req.DailyDeliveryCapacity.Value;
        if (req.ServiceRadiusKm    is not null) rider.ServiceRadiusKm     = req.ServiceRadiusKm.Value;
        if (req.PrimaryStoreId     is not null) rider.PrimaryStoreId      = req.PrimaryStoreId;

        rider.UpdatedAt = now;
        rider.UpdatedBy = command.ActorId;

        await _db.SaveChangesAsync(ct);
        var dto = await CreateRiderHandler.LoadEnrichedAsync(_db, rider, ct);
        return RiderDtoFinancialMask.Apply(dto, _user);
    }
}

public sealed class UpdateRiderRequestValidator : AbstractValidator<UpdateRiderRequest>
{
    public UpdateRiderRequestValidator()
    {
        // Status is the only privileged free-string field still settable here; whitelist it.
        // (KYC status is no longer assignable — it flows through verify/reject only.)
        RuleFor(x => x.Status)
            .Must(s => new[] { RiderStatus.Active, RiderStatus.Suspended, RiderStatus.Terminated }.Contains(s))
            .When(x => x.Status is not null)
            .WithMessage("Invalid status.");
        RuleFor(x => x.EmploymentType)
            .Must(t => new[] { RiderEmploymentType.Employee, RiderEmploymentType.Contractor, RiderEmploymentType.Gig, RiderEmploymentType.Outsourced }.Contains(t))
            .When(x => x.EmploymentType is not null)
            .WithMessage("Invalid employment_type.");
        RuleFor(x => x.VehicleType)
            .Must(t => new[] { RiderVehicleType.TwoWheeler, RiderVehicleType.ThreeWheeler, RiderVehicleType.FourWheeler, RiderVehicleType.Cycle, RiderVehicleType.Foot }.Contains(t))
            .When(x => x.VehicleType is not null)
            .WithMessage("Invalid vehicle_type.");
        RuleFor(x => x.DailyPickupCapacity).GreaterThanOrEqualTo(0).When(x => x.DailyPickupCapacity is not null);
        RuleFor(x => x.DailyDeliveryCapacity).GreaterThanOrEqualTo(0).When(x => x.DailyDeliveryCapacity is not null);
        RuleFor(x => x.ServiceRadiusKm).GreaterThan(0).When(x => x.ServiceRadiusKm is not null);
    }
}

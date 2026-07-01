using FluentValidation;
using operations.Application.Logistics.PartnerDispatch.Dtos;

namespace operations.Application.Logistics.PartnerDispatch.Validators;

// Run as endpoint filters (ValidationFilter<T>) against the request DTOs bound by the route.
// Registered by AddValidatorsFromAssembly in operations.Application.

public sealed class AssignPartnerDispatchValidator : AbstractValidator<AssignPartnerDispatchRequest>
{
    public AssignPartnerDispatchValidator()
    {
        RuleFor(x => x.PartnerBookingId).NotEmpty().WithMessage("PartnerBookingId is required.");
        RuleFor(x => x.PartnerId).NotEmpty().WithMessage("PartnerId is required.");
        RuleFor(x => x.RiderId)
            .NotEmpty()
            .When(x => x.RiderId.HasValue)
            .WithMessage("RiderId must be a non-empty GUID when supplied.");
        RuleFor(x => x.PickupOtp).MaximumLength(10);
        RuleFor(x => x.DropOtp).MaximumLength(10);
    }
}

public sealed class UpdatePartnerDispatchStatusValidator
    : AbstractValidator<UpdatePartnerDispatchStatusRequest>
{
    public UpdatePartnerDispatchStatusValidator()
    {
        RuleFor(x => x.LastKnownLat)
            .InclusiveBetween(-90, 90)
            .When(x => x.LastKnownLat.HasValue)
            .WithMessage("Latitude must be between -90 and 90.");
        RuleFor(x => x.LastKnownLng)
            .InclusiveBetween(-180, 180)
            .When(x => x.LastKnownLng.HasValue)
            .WithMessage("Longitude must be between -180 and 180.");

        // A location ping requires BOTH coordinates.
        RuleFor(x => x.LastKnownLng)
            .NotNull()
            .When(x => x.LastKnownLat.HasValue)
            .WithMessage("LastKnownLng is required when LastKnownLat is supplied.");
        RuleFor(x => x.LastKnownLat)
            .NotNull()
            .When(x => x.LastKnownLng.HasValue)
            .WithMessage("LastKnownLat is required when LastKnownLng is supplied.");

        RuleFor(x => x.ProofPhotoUrl).MaximumLength(1000);
        RuleFor(x => x.ProofSignatureUrl).MaximumLength(1000);
    }
}

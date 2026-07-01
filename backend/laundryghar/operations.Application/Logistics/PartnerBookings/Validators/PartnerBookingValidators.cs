using FluentValidation;
using operations.Application.Logistics.PartnerBookings.Dtos;

namespace operations.Application.Logistics.PartnerBookings.Validators;

// Runs as an endpoint filter (ValidationFilter<T>) against the request DTO bound by the route.
// Registered by AddValidatorsFromAssembly in operations.Application.

public sealed class CreatePartnerBookingValidator : AbstractValidator<CreatePartnerBookingRequest>
{
    public CreatePartnerBookingValidator()
    {
        RuleFor(x => x.Pickup).NotNull().SetValidator(new PartnerBookingLocationValidator());
        RuleFor(x => x.Drop).NotNull().SetValidator(new PartnerBookingLocationValidator());
        RuleFor(x => x.QuotedFare)
            .GreaterThanOrEqualTo(0m)
            .When(x => x.QuotedFare.HasValue)
            .WithMessage("Quoted fare must be zero or positive.");
    }
}

public sealed class PartnerBookingLocationValidator : AbstractValidator<PartnerBookingLocation>
{
    public PartnerBookingLocationValidator()
    {
        RuleFor(x => x.Address)
            .NotEmpty()
            .WithMessage("Address is required.");
        RuleFor(x => x.Lat)
            .InclusiveBetween(-90, 90)
            .When(x => x.Lat.HasValue)
            .WithMessage("Latitude must be between -90 and 90.");
        RuleFor(x => x.Lng)
            .InclusiveBetween(-180, 180)
            .When(x => x.Lng.HasValue)
            .WithMessage("Longitude must be between -180 and 180.");
    }
}

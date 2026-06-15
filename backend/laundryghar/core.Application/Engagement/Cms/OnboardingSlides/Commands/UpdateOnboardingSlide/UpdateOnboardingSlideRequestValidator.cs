using core.Application.Engagement.Cms.OnboardingSlides.Common;
using core.Application.Engagement.Cms.Dtos;
using FluentValidation;
using Microsoft.Extensions.Hosting;

namespace core.Application.Engagement.Cms.OnboardingSlides.Commands.UpdateOnboardingSlide;

// Validates the bound request body (the endpoint composes the command from route id + user + body).
// Run by ValidationFilter<UpdateOnboardingSlideRequest> at the endpoint.
public sealed class UpdateOnboardingSlideRequestValidator : AbstractValidator<UpdateOnboardingSlideRequest>
{
    public UpdateOnboardingSlideRequestValidator(IHostEnvironment env) =>
        Include(new OnboardingSlideFieldsValidator<UpdateOnboardingSlideRequest>(env));
}

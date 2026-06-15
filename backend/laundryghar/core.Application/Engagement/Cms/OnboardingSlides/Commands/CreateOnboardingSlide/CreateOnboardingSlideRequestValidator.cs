using core.Application.Engagement.Cms.OnboardingSlides.Common;
using core.Application.Engagement.Cms.Dtos;
using FluentValidation;
using Microsoft.Extensions.Hosting;

namespace core.Application.Engagement.Cms.OnboardingSlides.Commands.CreateOnboardingSlide;

// Validates the bound request body (the endpoint composes the command from it). Run by
// ValidationFilter<CreateOnboardingSlideRequest> at the endpoint.
public sealed class CreateOnboardingSlideRequestValidator : AbstractValidator<CreateOnboardingSlideRequest>
{
    public CreateOnboardingSlideRequestValidator(IHostEnvironment env) =>
        Include(new OnboardingSlideFieldsValidator<CreateOnboardingSlideRequest>(env));
}

using core.Application.Engagement.Cms.AppBanners.Common;
using core.Application.Engagement.Cms.Dtos;
using FluentValidation;
using Microsoft.Extensions.Hosting;

namespace core.Application.Engagement.Cms.AppBanners.Commands.CreateAppBanner;

// Validates the bound request body (the endpoint composes the command from it). Run by
// ValidationFilter<CreateAppBannerRequest> at the endpoint.
public sealed class CreateAppBannerRequestValidator : AbstractValidator<CreateAppBannerRequest>
{
    public CreateAppBannerRequestValidator(IHostEnvironment env) =>
        Include(new AppBannerFieldsValidator<CreateAppBannerRequest>(env));
}

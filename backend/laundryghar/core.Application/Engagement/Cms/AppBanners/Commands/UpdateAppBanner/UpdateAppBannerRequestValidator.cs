using core.Application.Engagement.Cms.AppBanners.Common;
using core.Application.Engagement.Cms.Dtos;
using FluentValidation;
using Microsoft.Extensions.Hosting;

namespace core.Application.Engagement.Cms.AppBanners.Commands.UpdateAppBanner;

// Validates the bound request body (the endpoint composes the command from route id + user + body).
// Run by ValidationFilter<UpdateAppBannerRequest> at the endpoint.
public sealed class UpdateAppBannerRequestValidator : AbstractValidator<UpdateAppBannerRequest>
{
    public UpdateAppBannerRequestValidator(IHostEnvironment env) =>
        Include(new AppBannerFieldsValidator<UpdateAppBannerRequest>(env));
}

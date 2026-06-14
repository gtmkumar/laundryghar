using core.Application.Engagement.Cms.AppBanners.Common;
using core.Application.Engagement.Cms.Dtos;
using FluentValidation;
using Microsoft.Extensions.Hosting;

namespace core.Application.Engagement.Cms.AppBanners.Commands.UpdateAppBanner;

public sealed class UpdateAppBannerCommandValidator : AbstractValidator<UpdateAppBannerCommand>
{
    public UpdateAppBannerCommandValidator(IHostEnvironment env) =>
        RuleFor(x => x.Request).SetValidator(new AppBannerFieldsValidator<UpdateAppBannerRequest>(env));
}

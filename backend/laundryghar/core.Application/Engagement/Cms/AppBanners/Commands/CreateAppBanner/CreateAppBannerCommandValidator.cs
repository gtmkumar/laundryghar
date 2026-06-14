using core.Application.Engagement.Cms.AppBanners.Common;
using core.Application.Engagement.Cms.Dtos;
using FluentValidation;
using Microsoft.Extensions.Hosting;

namespace core.Application.Engagement.Cms.AppBanners.Commands.CreateAppBanner;

public sealed class CreateAppBannerCommandValidator : AbstractValidator<CreateAppBannerCommand>
{
    public CreateAppBannerCommandValidator(IHostEnvironment env) =>
        RuleFor(x => x.Request).SetValidator(new AppBannerFieldsValidator<CreateAppBannerRequest>(env));
}

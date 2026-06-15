using core.Application.Common.Validation;
using core.Application.Engagement.Cms.Dtos;
using FluentValidation;

namespace core.Application.Engagement.Cms.MobileAppConfigs.Common;

internal static class MobileAppConfigRules
{
    internal static readonly string[] ValidPlatforms = ["android", "ios", "web"];
}

/// <summary>Rules common to create and update, written once against <see cref="MobileAppConfigFields"/>.
/// The thin per-command validators delegate to this via <c>Include</c>. Unlike onboarding slides
/// these rules carry no URL checks, so no host environment is needed.</summary>
public sealed class MobileAppConfigFieldsValidator<T> : AbstractValidator<T>
    where T : MobileAppConfigFields
{
    public MobileAppConfigFieldsValidator()
    {
        RuleFor(x => x.Platform).NotEmpty()
            .Must(p => MobileAppConfigRules.ValidPlatforms.Contains(p))
            .WithMessage("platform must be one of: android, ios, web");
        RuleFor(x => x.ConfigKey).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ConfigValue).NotEmpty().MustBeJson();
        RuleFor(x => x.AppType).NotEmpty().MaximumLength(20);
        RuleFor(x => x.RolloutPercent)
            .InclusiveBetween((short)0, (short)100)
            .When(x => x.RolloutPercent.HasValue)
            .WithMessage("rollout_percent must be between 0 and 100");
    }
}

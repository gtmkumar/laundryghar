using core.Application.Engagement.Cms.MobileAppConfigs.Common;
using core.Application.Engagement.Cms.Dtos;
using FluentValidation;

namespace core.Application.Engagement.Cms.MobileAppConfigs.Commands.CreateMobileAppConfig;

// Validates the bound request body (the endpoint composes the command from it). Run by
// ValidationFilter<CreateMobileAppConfigRequest> at the endpoint.
public sealed class CreateMobileAppConfigRequestValidator : AbstractValidator<CreateMobileAppConfigRequest>
{
    public CreateMobileAppConfigRequestValidator() =>
        Include(new MobileAppConfigFieldsValidator<CreateMobileAppConfigRequest>());
}

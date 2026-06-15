using core.Application.Engagement.Cms.MobileAppConfigs.Common;
using core.Application.Engagement.Cms.Dtos;
using FluentValidation;

namespace core.Application.Engagement.Cms.MobileAppConfigs.Commands.UpdateMobileAppConfig;

// Validates the bound request body (the endpoint composes the command from route id + user + body).
// Run by ValidationFilter<UpdateMobileAppConfigRequest> at the endpoint.
public sealed class UpdateMobileAppConfigRequestValidator : AbstractValidator<UpdateMobileAppConfigRequest>
{
    public UpdateMobileAppConfigRequestValidator() =>
        Include(new MobileAppConfigFieldsValidator<UpdateMobileAppConfigRequest>());
}

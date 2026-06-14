using FluentValidation;

namespace core.Application.NotificationTemplates.Commands.CreateNotificationTemplate;

public class CreateNotificationTemplateCommandValidator : AbstractValidator<CreateNotificationTemplateCommand>
{
    public CreateNotificationTemplateCommandValidator()
    {
        RuleFor(v => v.BrandId).NotEmpty();
        RuleFor(v => v.Code).NotEmpty().MaximumLength(100);
        RuleFor(v => v.Name).NotEmpty().MaximumLength(200);
        RuleFor(v => v.Channel).NotEmpty();
        RuleFor(v => v.Locale).NotEmpty();
        RuleFor(v => v.BodyTemplate).NotEmpty();
    }
}

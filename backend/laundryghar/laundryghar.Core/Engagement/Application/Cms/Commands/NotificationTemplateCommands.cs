using FluentValidation;
using laundryghar.Engagement.Application.Cms.Dtos;
using MediatR;

namespace laundryghar.Engagement.Application.Cms.Commands;

// ── Create ─────────────────────────────────────────────────────────────────────

public sealed record CreateNotificationTemplateCommand(
    CreateNotificationTemplateRequest Request, Guid? ActorId) : IRequest<NotificationTemplateDto>;

public sealed class CreateNotificationTemplateHandler
    : IRequestHandler<CreateNotificationTemplateCommand, NotificationTemplateDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public CreateNotificationTemplateHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<NotificationTemplateDto> Handle(
        CreateNotificationTemplateCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        var entity = new NotificationTemplate
        {
            Id                   = Guid.NewGuid(),
            BrandId              = brandId,
            Code                 = req.Code,
            Name                 = req.Name,
            Description          = req.Description,
            Channel              = req.Channel,
            Category             = req.Category,
            Locale               = req.Locale,
            SubjectTemplate      = req.SubjectTemplate,
            BodyTemplate         = req.BodyTemplate,
            SmsSenderId          = req.SmsSenderId,
            WhatsAppTemplateName = req.WhatsAppTemplateName,
            WhatsAppTemplateId   = req.WhatsAppTemplateId,
            WhatsAppLangCode     = req.WhatsAppLangCode,
            WhatsAppNamespace    = req.WhatsAppNamespace,
            PushTitleTemplate    = req.PushTitleTemplate,
            PushActionDeeplink   = req.PushActionDeeplink,
            PushIconUrl          = req.PushIconUrl,
            PushSound            = req.PushSound,
            Variables            = req.Variables,
            VersionNumber        = req.VersionNumber > 0 ? req.VersionNumber : 1,
            IsTransactional      = req.IsTransactional,
            IsActive             = req.IsActive,
            Status               = "active",
            CreatedAt            = now,
            UpdatedAt            = now,
            CreatedBy            = cmd.ActorId,
            UpdatedBy            = cmd.ActorId,
        };

        _db.NotificationTemplates.Add(entity);
        await _db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    internal static NotificationTemplateDto ToDto(NotificationTemplate e) => new(
        e.Id, e.BrandId, e.Code, e.Name, e.Description,
        e.Channel, e.Category, e.Locale,
        e.SubjectTemplate, e.BodyTemplate,
        e.SmsSenderId, e.WhatsAppTemplateName, e.WhatsAppTemplateId,
        e.PushTitleTemplate, e.PushActionDeeplink,
        e.Variables, e.VersionNumber, e.IsTransactional, e.IsActive,
        e.ApprovedAt, e.Status, e.CreatedAt, e.UpdatedAt);
}

// ── Update ─────────────────────────────────────────────────────────────────────

public sealed record UpdateNotificationTemplateCommand(
    Guid Id, UpdateNotificationTemplateRequest Request, Guid? ActorId) : IRequest<NotificationTemplateDto?>;

public sealed class UpdateNotificationTemplateHandler
    : IRequestHandler<UpdateNotificationTemplateCommand, NotificationTemplateDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateNotificationTemplateHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<NotificationTemplateDto?> Handle(
        UpdateNotificationTemplateCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var entity = await _db.NotificationTemplates
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (entity is null) return null;

        var req = cmd.Request;
        entity.Name                 = req.Name;
        entity.Description          = req.Description;
        entity.SubjectTemplate      = req.SubjectTemplate;
        entity.BodyTemplate         = req.BodyTemplate;
        entity.SmsSenderId          = req.SmsSenderId;
        entity.WhatsAppTemplateName = req.WhatsAppTemplateName;
        entity.WhatsAppTemplateId   = req.WhatsAppTemplateId;
        entity.WhatsAppLangCode     = req.WhatsAppLangCode;
        entity.WhatsAppNamespace    = req.WhatsAppNamespace;
        entity.PushTitleTemplate    = req.PushTitleTemplate;
        entity.PushActionDeeplink   = req.PushActionDeeplink;
        entity.PushIconUrl          = req.PushIconUrl;
        entity.PushSound            = req.PushSound;
        entity.Variables            = req.Variables;
        entity.IsTransactional      = req.IsTransactional;
        entity.IsActive             = req.IsActive;
        entity.Status               = req.Status;
        entity.UpdatedAt            = DateTimeOffset.UtcNow;
        entity.UpdatedBy            = cmd.ActorId;
        entity.VersionNumber++;

        await _db.SaveChangesAsync(ct);
        return CreateNotificationTemplateHandler.ToDto(entity);
    }
}

// ── Delete ─────────────────────────────────────────────────────────────────────

public sealed record DeleteNotificationTemplateCommand(Guid Id, Guid? ActorId) : IRequest<bool>;

public sealed class DeleteNotificationTemplateHandler
    : IRequestHandler<DeleteNotificationTemplateCommand, bool>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public DeleteNotificationTemplateHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<bool> Handle(DeleteNotificationTemplateCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var entity = await _db.NotificationTemplates
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (entity is null) return false;

        entity.Status    = "archived";
        entity.IsActive  = false;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = cmd.ActorId;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

// ── Validators ─────────────────────────────────────────────────────────────────

public sealed class CreateNotificationTemplateValidator
    : AbstractValidator<CreateNotificationTemplateCommand>
{
    private static readonly string[] ValidChannels =
        ["sms", "whatsapp", "email", "push", "in_app", "voice"];

    public CreateNotificationTemplateValidator()
    {
        RuleFor(x => x.Request.Code).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.Channel).NotEmpty().Must(c => ValidChannels.Contains(c))
            .WithMessage("channel must be one of: sms, whatsapp, email, push, in_app, voice");
        RuleFor(x => x.Request.Category).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Request.Locale).NotEmpty().MaximumLength(10);
        RuleFor(x => x.Request.BodyTemplate).NotEmpty();
        RuleFor(x => x.Request.Variables).NotEmpty();
    }
}

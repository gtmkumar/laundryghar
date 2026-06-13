using laundryghar.Catalog.Infrastructure.Auth;
using laundryghar.Catalog.Infrastructure.Services;
using FluentValidation;
using laundryghar.Catalog.Application.Catalog.Dtos;
using MediatR;

namespace laundryghar.Catalog.Application.Catalog.Commands;

public sealed record CreateServiceCommand(
    CreateServiceRequest Request,
    Guid? ActorId
) : IRequest<ServiceDto>;

public sealed class CreateServiceHandler : IRequestHandler<CreateServiceCommand, ServiceDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public CreateServiceHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<ServiceDto> Handle(CreateServiceCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        var entity = new Service
        {
            Id                  = Guid.NewGuid(),
            BrandId             = brandId,
            CategoryId          = req.CategoryId,
            Code                = req.Code,
            Name                = req.Name,
            NameLocalized       = req.NameLocalized,
            Description         = req.Description,
            PricingModel        = req.PricingModel,
            BaseTatHours        = req.BaseTatHours,
            ExpressTatHours     = req.ExpressTatHours,
            ExpressMultiplier   = req.ExpressMultiplier,
            IsExpressAvailable  = req.IsExpressAvailable,
            RequiresInspection  = req.RequiresInspection,
            RequiresQc          = req.RequiresQc,
            IconUrl             = req.IconUrl,
            DisplayOrder        = req.DisplayOrder,
            Status              = "active",
            CreatedAt           = now,
            UpdatedAt           = now,
            CreatedBy           = cmd.ActorId,
            UpdatedBy           = cmd.ActorId,
            Version             = 1
        };

        _db.Services.Add(entity);
        await _db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    internal static ServiceDto ToDto(Service e) => new(
        e.Id, e.BrandId, e.CategoryId, e.Code, e.Name, e.NameLocalized,
        e.Description, e.PricingModel, e.BaseTatHours, e.ExpressTatHours,
        e.ExpressMultiplier, e.IsExpressAvailable, e.RequiresInspection,
        e.RequiresQc, e.IconUrl, e.DisplayOrder, e.Status, e.CreatedAt, e.UpdatedAt);
}

public sealed record UpdateServiceCommand(Guid Id, UpdateServiceRequest Request, Guid? ActorId) : IRequest<ServiceDto?>;

public sealed class UpdateServiceHandler : IRequestHandler<UpdateServiceCommand, ServiceDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateServiceHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<ServiceDto?> Handle(UpdateServiceCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.Services
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (e is null || e.DeletedAt != null) return null;

        var req = cmd.Request;
        e.Name               = req.Name;
        e.NameLocalized      = req.NameLocalized;
        e.Description        = req.Description;
        e.PricingModel       = req.PricingModel;
        e.BaseTatHours       = req.BaseTatHours;
        e.ExpressTatHours    = req.ExpressTatHours;
        e.ExpressMultiplier  = req.ExpressMultiplier;
        e.IsExpressAvailable = req.IsExpressAvailable;
        e.RequiresInspection = req.RequiresInspection;
        e.RequiresQc         = req.RequiresQc;
        e.IconUrl            = req.IconUrl;
        e.DisplayOrder       = req.DisplayOrder;
        e.Status             = req.Status;
        e.UpdatedAt          = DateTimeOffset.UtcNow;
        e.UpdatedBy          = cmd.ActorId;
        e.Version++;

        await _db.SaveChangesAsync(ct);
        return CreateServiceHandler.ToDto(e);
    }
}

public sealed record DeleteServiceCommand(Guid Id, Guid? ActorId) : IRequest<bool>;

public sealed class DeleteServiceHandler : IRequestHandler<DeleteServiceCommand, bool>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public DeleteServiceHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<bool> Handle(DeleteServiceCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.Services
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (e is null || e.DeletedAt != null) return false;

        // Soft-delete must also move status off 'active' so status-keyed reports don't
        // miscount it. services CHECK is ('active','disabled') — 'disabled' is terminal.
        e.Status    = "disabled";
        e.DeletedAt = DateTimeOffset.UtcNow;
        e.UpdatedAt = DateTimeOffset.UtcNow;
        e.UpdatedBy = cmd.ActorId;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class CreateServiceValidator : AbstractValidator<CreateServiceCommand>
{
    public CreateServiceValidator()
    {
        RuleFor(x => x.Request.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.NameLocalized).NotEmpty().MaximumLength(200).MustBeJsonObject();
        RuleFor(x => x.Request.CategoryId).NotEmpty();
        RuleFor(x => x.Request.PricingModel).NotEmpty();
    }
}

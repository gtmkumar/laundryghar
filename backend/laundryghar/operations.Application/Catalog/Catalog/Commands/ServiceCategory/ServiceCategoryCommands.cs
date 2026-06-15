using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Catalog.Catalog.Dtos;
using operations.Application.Catalog.Common;
using operations.Application.Common.Interfaces;

namespace operations.Application.Catalog.Catalog.Commands.ServiceCategory;

// ── Create ────────────────────────────────────────────────────────────────────

public sealed record CreateServiceCategoryCommand(
    CreateServiceCategoryRequest Request,
    Guid? ActorId
) : ICommand<ServiceCategoryDto>;

public sealed class CreateServiceCategoryHandler : ICommandHandler<CreateServiceCategoryCommand, ServiceCategoryDto>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public CreateServiceCategoryHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<ServiceCategoryDto> HandleAsync(CreateServiceCategoryCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();

        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        var entity = new laundryghar.SharedDataModel.Entities.CustomerCatalog.ServiceCategory
        {
            Id                   = Guid.NewGuid(),
            BrandId              = brandId,
            Code                 = req.Code,
            Name                 = req.Name,
            NameLocalized        = req.NameLocalized,
            Description          = req.Description,
            IconUrl              = req.IconUrl,
            ImageUrl             = req.ImageUrl,
            ColorHex             = req.ColorHex,
            DisplayOrder         = req.DisplayOrder,
            IsVisibleMobile      = req.IsVisibleMobile,
            IsVisiblePos         = req.IsVisiblePos,
            RequiresWarehouseCap = req.RequiresWarehouseCap ?? [],
            Status               = "active",
            CreatedAt            = now,
            UpdatedAt            = now,
            CreatedBy            = cmd.ActorId,
            UpdatedBy            = cmd.ActorId,
            Version              = 1
        };

        _db.ServiceCategories.Add(entity);
        await _db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    internal static ServiceCategoryDto ToDto(laundryghar.SharedDataModel.Entities.CustomerCatalog.ServiceCategory e) => new(
        e.Id, e.BrandId, e.Code, e.Name, e.NameLocalized, e.Description,
        e.IconUrl, e.ImageUrl, e.ColorHex, e.DisplayOrder,
        e.IsVisibleMobile, e.IsVisiblePos, e.Status, e.CreatedAt, e.UpdatedAt);
}

// ── Update ────────────────────────────────────────────────────────────────────

public sealed record UpdateServiceCategoryCommand(
    Guid Id,
    UpdateServiceCategoryRequest Request,
    Guid? ActorId
) : ICommand<ServiceCategoryDto?>;

public sealed class UpdateServiceCategoryHandler : ICommandHandler<UpdateServiceCategoryCommand, ServiceCategoryDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateServiceCategoryHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<ServiceCategoryDto?> HandleAsync(UpdateServiceCategoryCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        // Brand predicate: cross-brand row returns NotFound (defense-in-depth on top of RLS).
        var entity = await _db.ServiceCategories
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (entity is null || entity.DeletedAt != null) return null;

        var req = cmd.Request;
        entity.Name             = req.Name;
        entity.NameLocalized    = req.NameLocalized;
        entity.Description      = req.Description;
        entity.IconUrl          = req.IconUrl;
        entity.ImageUrl         = req.ImageUrl;
        entity.ColorHex         = req.ColorHex;
        entity.DisplayOrder     = req.DisplayOrder;
        entity.IsVisibleMobile  = req.IsVisibleMobile;
        entity.IsVisiblePos     = req.IsVisiblePos;
        entity.Status           = req.Status;
        entity.UpdatedAt        = DateTimeOffset.UtcNow;
        entity.UpdatedBy        = cmd.ActorId;
        entity.Version++;

        await _db.SaveChangesAsync(ct);
        return CreateServiceCategoryHandler.ToDto(entity);
    }
}

// ── Soft Delete ───────────────────────────────────────────────────────────────

public sealed record DeleteServiceCategoryCommand(Guid Id, Guid? ActorId) : ICommand<bool>;

public sealed class DeleteServiceCategoryHandler : ICommandHandler<DeleteServiceCategoryCommand, bool>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public DeleteServiceCategoryHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<bool> HandleAsync(DeleteServiceCategoryCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var entity = await _db.ServiceCategories
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (entity is null || entity.DeletedAt != null) return false;

        // Soft-delete must also move status off 'active' so status-keyed reports don't
        // miscount it. service_categories CHECK is ('active','disabled','seasonal') —
        // 'disabled' is the archived/retired equivalent.
        entity.Status    = "disabled";
        entity.DeletedAt = DateTimeOffset.UtcNow;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = cmd.ActorId;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class CreateServiceCategoryValidator : AbstractValidator<CreateServiceCategoryRequest>
{
    public CreateServiceCategoryValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.NameLocalized).NotEmpty().MustBeJsonObject();
    }
}

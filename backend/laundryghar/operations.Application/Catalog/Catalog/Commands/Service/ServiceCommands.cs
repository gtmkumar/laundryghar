using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Catalog.Catalog.Dtos;
using operations.Application.Catalog.Common;
using operations.Application.Common.Interfaces;

namespace operations.Application.Catalog.Catalog.Commands.Service;

public sealed record CreateServiceCommand(
    CreateServiceRequest Request,
    Guid? ActorId
) : ICommand<ServiceDto>;

public sealed class CreateServiceHandler : ICommandHandler<CreateServiceCommand, ServiceDto>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public CreateServiceHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<ServiceDto> HandleAsync(CreateServiceCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        var entity = new laundryghar.SharedDataModel.Entities.CustomerCatalog.Service
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

    internal static ServiceDto ToDto(laundryghar.SharedDataModel.Entities.CustomerCatalog.Service e) => new(
        e.Id, e.BrandId, e.CategoryId, e.Code, e.Name, e.NameLocalized,
        e.Description, e.PricingModel, e.BaseTatHours, e.ExpressTatHours,
        e.ExpressMultiplier, e.IsExpressAvailable, e.RequiresInspection,
        e.RequiresQc, e.IconUrl, e.DisplayOrder, e.Status, e.CreatedAt, e.UpdatedAt);
}

public sealed record UpdateServiceCommand(Guid Id, UpdateServiceRequest Request, Guid? ActorId) : ICommand<ServiceDto?>;

public sealed class UpdateServiceHandler : ICommandHandler<UpdateServiceCommand, ServiceDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateServiceHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<ServiceDto?> HandleAsync(UpdateServiceCommand cmd, CancellationToken ct)
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

public sealed record DeleteServiceCommand(Guid Id, Guid? ActorId) : ICommand<bool>;

public sealed class DeleteServiceHandler : ICommandHandler<DeleteServiceCommand, bool>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public DeleteServiceHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<bool> HandleAsync(DeleteServiceCommand cmd, CancellationToken ct)
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

public sealed class CreateServiceValidator : AbstractValidator<CreateServiceRequest>
{
    public CreateServiceValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameLocalized).NotEmpty().MaximumLength(200).MustBeJsonObject();
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.PricingModel).NotEmpty();
    }
}

using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Catalog.Catalog.Dtos;
using operations.Application.Catalog.Common;
using operations.Application.Common.Interfaces;

namespace operations.Application.Catalog.Catalog.Commands.FabricType;

public sealed record CreateFabricTypeCommand(CreateFabricTypeRequest Request, Guid? ActorId) : ICommand<FabricTypeDto>;

public sealed class CreateFabricTypeHandler : ICommandHandler<CreateFabricTypeCommand, FabricTypeDto>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public CreateFabricTypeHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<FabricTypeDto> HandleAsync(CreateFabricTypeCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        var e = new laundryghar.SharedDataModel.Entities.CustomerCatalog.FabricType
        {
            Id                   = Guid.NewGuid(),
            BrandId              = brandId,
            Code                 = req.Code,
            Name                 = req.Name,
            NameLocalized        = req.NameLocalized,
            Description          = req.Description,
            CareInstructions     = req.CareInstructions,
            PriceMultiplier      = req.PriceMultiplier,
            RequiresSpecialCare  = req.RequiresSpecialCare,
            DisplayOrder         = req.DisplayOrder,
            Status               = "active",
            CreatedAt            = now,
            UpdatedAt            = now,
            CreatedBy            = cmd.ActorId,
            UpdatedBy            = cmd.ActorId
        };

        _db.FabricTypes.Add(e);
        await _db.SaveChangesAsync(ct);
        return ToDto(e);
    }

    internal static FabricTypeDto ToDto(laundryghar.SharedDataModel.Entities.CustomerCatalog.FabricType e) => new(
        e.Id, e.BrandId, e.Code, e.Name, e.NameLocalized, e.Description,
        e.CareInstructions, e.PriceMultiplier, e.RequiresSpecialCare,
        e.DisplayOrder, e.Status, e.CreatedAt, e.UpdatedAt);
}

public sealed record UpdateFabricTypeCommand(Guid Id, UpdateFabricTypeRequest Request, Guid? ActorId) : ICommand<FabricTypeDto?>;

public sealed class UpdateFabricTypeHandler : ICommandHandler<UpdateFabricTypeCommand, FabricTypeDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateFabricTypeHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<FabricTypeDto?> HandleAsync(UpdateFabricTypeCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.FabricTypes
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (e is null || e.DeletedAt != null) return null;

        var req = cmd.Request;
        e.Name               = req.Name;
        e.NameLocalized      = req.NameLocalized;
        e.Description        = req.Description;
        e.CareInstructions   = req.CareInstructions;
        e.PriceMultiplier    = req.PriceMultiplier;
        e.RequiresSpecialCare = req.RequiresSpecialCare;
        e.DisplayOrder       = req.DisplayOrder;
        e.Status             = req.Status;
        e.UpdatedAt          = DateTimeOffset.UtcNow;
        e.UpdatedBy          = cmd.ActorId;

        await _db.SaveChangesAsync(ct);
        return CreateFabricTypeHandler.ToDto(e);
    }
}

public sealed record DeleteFabricTypeCommand(Guid Id, Guid? ActorId) : ICommand<bool>;

public sealed class DeleteFabricTypeHandler : ICommandHandler<DeleteFabricTypeCommand, bool>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public DeleteFabricTypeHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<bool> HandleAsync(DeleteFabricTypeCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.FabricTypes
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (e is null || e.DeletedAt != null) return false;

        // Soft-delete must also move status off 'active' so status-keyed reports don't
        // miscount it. fabric_types CHECK is ('active','disabled') — 'disabled' is terminal.
        e.Status    = "disabled";
        e.DeletedAt = DateTimeOffset.UtcNow;
        e.UpdatedAt = DateTimeOffset.UtcNow;
        e.UpdatedBy = cmd.ActorId;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class CreateFabricTypeValidator : AbstractValidator<CreateFabricTypeRequest>
{
    public CreateFabricTypeValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameLocalized).NotEmpty().MaximumLength(200).MustBeJsonObject();
        RuleFor(x => x.PriceMultiplier).GreaterThan(0);
    }
}

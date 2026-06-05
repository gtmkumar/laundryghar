using FluentValidation;
using laundryghar.Catalog.Application.Catalog.Dtos;
using MediatR;

namespace laundryghar.Catalog.Application.Catalog.Commands;

public sealed record CreateFabricTypeCommand(CreateFabricTypeRequest Request, Guid? ActorId) : IRequest<FabricTypeDto>;

public sealed class CreateFabricTypeHandler : IRequestHandler<CreateFabricTypeCommand, FabricTypeDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public CreateFabricTypeHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<FabricTypeDto> Handle(CreateFabricTypeCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        var e = new FabricType
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

    internal static FabricTypeDto ToDto(FabricType e) => new(
        e.Id, e.BrandId, e.Code, e.Name, e.NameLocalized, e.Description,
        e.CareInstructions, e.PriceMultiplier, e.RequiresSpecialCare,
        e.DisplayOrder, e.Status, e.CreatedAt, e.UpdatedAt);
}

public sealed record UpdateFabricTypeCommand(Guid Id, UpdateFabricTypeRequest Request, Guid? ActorId) : IRequest<FabricTypeDto?>;

public sealed class UpdateFabricTypeHandler : IRequestHandler<UpdateFabricTypeCommand, FabricTypeDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateFabricTypeHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<FabricTypeDto?> Handle(UpdateFabricTypeCommand cmd, CancellationToken ct)
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

public sealed record DeleteFabricTypeCommand(Guid Id, Guid? ActorId) : IRequest<bool>;

public sealed class DeleteFabricTypeHandler : IRequestHandler<DeleteFabricTypeCommand, bool>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public DeleteFabricTypeHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<bool> Handle(DeleteFabricTypeCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.FabricTypes
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (e is null || e.DeletedAt != null) return false;

        e.DeletedAt = DateTimeOffset.UtcNow;
        e.UpdatedAt = DateTimeOffset.UtcNow;
        e.UpdatedBy = cmd.ActorId;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class CreateFabricTypeValidator : AbstractValidator<CreateFabricTypeCommand>
{
    public CreateFabricTypeValidator()
    {
        RuleFor(x => x.Request.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.NameLocalized).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.PriceMultiplier).GreaterThan(0);
    }
}

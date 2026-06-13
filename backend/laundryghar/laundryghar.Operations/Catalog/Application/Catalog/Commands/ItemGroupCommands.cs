using laundryghar.Catalog.Infrastructure.Auth;
using laundryghar.Catalog.Infrastructure.Services;
using FluentValidation;
using laundryghar.Catalog.Application.Catalog.Dtos;
using MediatR;

namespace laundryghar.Catalog.Application.Catalog.Commands;

public sealed record CreateItemGroupCommand(CreateItemGroupRequest Request, Guid? ActorId) : IRequest<ItemGroupDto>;

public sealed class CreateItemGroupHandler : IRequestHandler<CreateItemGroupCommand, ItemGroupDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public CreateItemGroupHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<ItemGroupDto> Handle(CreateItemGroupCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        var e = new ItemGroup
        {
            Id              = Guid.NewGuid(),
            BrandId         = brandId,
            Code            = req.Code,
            Name            = req.Name,
            NameLocalized   = req.NameLocalized,
            IconUrl         = req.IconUrl,
            DisplayOrder    = req.DisplayOrder,
            IsVisibleMobile = req.IsVisibleMobile,
            Status          = "active",
            CreatedAt       = now,
            UpdatedAt       = now,
            CreatedBy       = cmd.ActorId,
            UpdatedBy       = cmd.ActorId
        };

        _db.ItemGroups.Add(e);
        await _db.SaveChangesAsync(ct);
        return ToDto(e);
    }

    internal static ItemGroupDto ToDto(ItemGroup e) => new(
        e.Id, e.BrandId, e.Code, e.Name, e.NameLocalized,
        e.IconUrl, e.DisplayOrder, e.IsVisibleMobile, e.Status, e.CreatedAt, e.UpdatedAt);
}

public sealed record UpdateItemGroupCommand(Guid Id, UpdateItemGroupRequest Request, Guid? ActorId) : IRequest<ItemGroupDto?>;

public sealed class UpdateItemGroupHandler : IRequestHandler<UpdateItemGroupCommand, ItemGroupDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateItemGroupHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<ItemGroupDto?> Handle(UpdateItemGroupCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.ItemGroups
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (e is null || e.DeletedAt != null) return null;

        var req = cmd.Request;
        e.Name            = req.Name;
        e.NameLocalized   = req.NameLocalized;
        e.IconUrl         = req.IconUrl;
        e.DisplayOrder    = req.DisplayOrder;
        e.IsVisibleMobile = req.IsVisibleMobile;
        e.Status          = req.Status;
        e.UpdatedAt       = DateTimeOffset.UtcNow;
        e.UpdatedBy       = cmd.ActorId;

        await _db.SaveChangesAsync(ct);
        return CreateItemGroupHandler.ToDto(e);
    }
}

public sealed record DeleteItemGroupCommand(Guid Id, Guid? ActorId) : IRequest<bool>;

public sealed class DeleteItemGroupHandler : IRequestHandler<DeleteItemGroupCommand, bool>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public DeleteItemGroupHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<bool> Handle(DeleteItemGroupCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.ItemGroups
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (e is null || e.DeletedAt != null) return false;

        // Soft-delete must also move status off 'active' so status-keyed reports don't
        // miscount it. item_groups CHECK is ('active','disabled') — 'disabled' is terminal.
        e.Status    = "disabled";
        e.DeletedAt = DateTimeOffset.UtcNow;
        e.UpdatedAt = DateTimeOffset.UtcNow;
        e.UpdatedBy = cmd.ActorId;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class CreateItemGroupValidator : AbstractValidator<CreateItemGroupCommand>
{
    public CreateItemGroupValidator()
    {
        RuleFor(x => x.Request.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.NameLocalized).NotEmpty().MaximumLength(200).MustBeJsonObject();
    }
}

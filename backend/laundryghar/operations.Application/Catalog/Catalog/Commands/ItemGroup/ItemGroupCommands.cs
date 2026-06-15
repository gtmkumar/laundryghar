using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Catalog.Catalog.Dtos;
using operations.Application.Catalog.Common;
using operations.Application.Common.Interfaces;

namespace operations.Application.Catalog.Catalog.Commands.ItemGroup;

public sealed record CreateItemGroupCommand(CreateItemGroupRequest Request, Guid? ActorId) : ICommand<ItemGroupDto>;

public sealed class CreateItemGroupHandler : ICommandHandler<CreateItemGroupCommand, ItemGroupDto>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public CreateItemGroupHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<ItemGroupDto> HandleAsync(CreateItemGroupCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        var e = new laundryghar.SharedDataModel.Entities.CustomerCatalog.ItemGroup
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

    internal static ItemGroupDto ToDto(laundryghar.SharedDataModel.Entities.CustomerCatalog.ItemGroup e) => new(
        e.Id, e.BrandId, e.Code, e.Name, e.NameLocalized,
        e.IconUrl, e.DisplayOrder, e.IsVisibleMobile, e.Status, e.CreatedAt, e.UpdatedAt);
}

public sealed record UpdateItemGroupCommand(Guid Id, UpdateItemGroupRequest Request, Guid? ActorId) : ICommand<ItemGroupDto?>;

public sealed class UpdateItemGroupHandler : ICommandHandler<UpdateItemGroupCommand, ItemGroupDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateItemGroupHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<ItemGroupDto?> HandleAsync(UpdateItemGroupCommand cmd, CancellationToken ct)
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

public sealed record DeleteItemGroupCommand(Guid Id, Guid? ActorId) : ICommand<bool>;

public sealed class DeleteItemGroupHandler : ICommandHandler<DeleteItemGroupCommand, bool>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public DeleteItemGroupHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<bool> HandleAsync(DeleteItemGroupCommand cmd, CancellationToken ct)
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

public sealed class CreateItemGroupValidator : AbstractValidator<CreateItemGroupRequest>
{
    public CreateItemGroupValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameLocalized).NotEmpty().MaximumLength(200).MustBeJsonObject();
    }
}

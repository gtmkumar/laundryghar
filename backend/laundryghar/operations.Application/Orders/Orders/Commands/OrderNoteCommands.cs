using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Orders.Orders.Dtos;

namespace operations.Application.Orders.Orders.Commands;

public sealed record CreateOrderNoteCommand(Guid OrderId, CreateOrderNoteRequest Request, Guid? ActorId)
    : ICommand<OrderNoteDto?>;

public sealed class CreateOrderNoteHandler : ICommandHandler<CreateOrderNoteCommand, OrderNoteDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public CreateOrderNoteHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<OrderNoteDto?> HandleAsync(CreateOrderNoteCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var now     = DateTimeOffset.UtcNow;

        var order = await _db.Orders
            .FirstOrDefaultAsync(o => o.Id == cmd.OrderId && o.BrandId == brandId, ct);
        if (order is null || order.DeletedAt != null) return null;

        if (!_user.IsWithinScope(brandId: order.BrandId, franchiseId: order.FranchiseId, storeId: order.StoreId, warehouseId: order.WarehouseId))
            throw new ForbiddenException("This order is outside your assigned scope.");

        var note = new OrderNote
        {
            Id             = Guid.NewGuid(),
            OrderId        = order.Id,
            OrderCreatedAt = order.CreatedAt,
            BrandId        = brandId,
            NoteType       = cmd.Request.NoteType,
            Visibility     = cmd.Request.Visibility,
            AuthorType     = "user",
            AuthorId       = cmd.ActorId,
            NoteText       = cmd.Request.NoteText,
            Attachments    = "[]",
            IsPinned       = cmd.Request.IsPinned,
            IsResolved     = false,
            CreatedAt      = now,
            CreatedBy      = cmd.ActorId
        };

        _db.OrderNotes.Add(note);
        await _db.SaveChangesAsync(ct);
        return ToDto(note);
    }

    internal static OrderNoteDto ToDto(OrderNote n) => new(
        n.Id, n.NoteType, n.Visibility, n.AuthorType, n.NoteText, n.IsPinned, n.CreatedAt);
}

public sealed record DeleteOrderNoteCommand(Guid OrderId, Guid NoteId, Guid? ActorId) : ICommand<bool>;

public sealed class DeleteOrderNoteHandler : ICommandHandler<DeleteOrderNoteCommand, bool>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public DeleteOrderNoteHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<bool> HandleAsync(DeleteOrderNoteCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var note = await _db.OrderNotes
            .FirstOrDefaultAsync(n => n.Id == cmd.NoteId && n.OrderId == cmd.OrderId && n.BrandId == brandId, ct);
        if (note is null || note.DeletedAt != null) return false;

        note.DeletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class CreateOrderNoteValidator : AbstractValidator<CreateOrderNoteCommand>
{
    private static readonly string[] AllowedNoteTypes =
        ["internal", "customer_facing", "complaint", "resolution", "flag"];
    private static readonly string[] AllowedVisibilities =
        ["staff", "customer", "platform"];

    public CreateOrderNoteValidator()
    {
        RuleFor(x => x.Request.NoteText).NotEmpty();
        RuleFor(x => x.Request.NoteType)
            .Must(t => AllowedNoteTypes.Contains(t))
            .WithMessage($"NoteType must be one of: {string.Join(", ", AllowedNoteTypes)}.");
        RuleFor(x => x.Request.Visibility)
            .Must(v => AllowedVisibilities.Contains(v))
            .WithMessage($"Visibility must be one of: {string.Join(", ", AllowedVisibilities)}.");
    }
}

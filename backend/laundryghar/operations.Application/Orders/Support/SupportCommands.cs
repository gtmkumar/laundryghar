using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.EngagementCms;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;

namespace operations.Application.Orders.Support;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public sealed record SupportTicketDto(
    Guid Id, string TicketNumber, string RequesterType, string? RequesterName,
    string Subject, string Category, string Priority, string Status,
    Guid? OrderId, DateTimeOffset LastMessageAt, DateTimeOffset CreatedAt);

public sealed record TicketMessageDto(
    Guid Id, string SenderType, Guid? SenderId, string Body, DateTimeOffset CreatedAt);

public sealed record SupportTicketDetailDto(SupportTicketDto Ticket, IReadOnlyList<TicketMessageDto> Messages);

public sealed record CreateTicketRequest(string Subject, string Message, string? Category = null, Guid? OrderId = null);
public sealed record PostMessageRequest(string Body);
public sealed record UpdateTicketRequest(string? Status = null, string? Priority = null, Guid? AssignedTo = null);

internal static class SupportMap
{
    internal static SupportTicketDto ToDto(SupportTicket t, string? requesterName = null) =>
        new(t.Id, t.TicketNumber, t.RequesterType, requesterName, t.Subject, t.Category,
            t.Priority, t.Status, t.OrderId, t.LastMessageAt, t.CreatedAt);
    internal static TicketMessageDto ToDto(TicketMessage m) =>
        new(m.Id, m.SenderType, m.SenderId, m.Body, m.CreatedAt);
}

// ── Create ───────────────────────────────────────────────────────────────────

/// <summary>Opens a ticket (with its first message) for a customer or rider.</summary>
public sealed record CreateTicketCommand(
    Guid BrandId, string RequesterType, Guid RequesterId,
    Guid? CustomerId, Guid? RiderId, CreateTicketRequest Request) : ICommand<SupportTicketDetailDto>;

public sealed class CreateTicketHandler : ICommandHandler<CreateTicketCommand, SupportTicketDetailDto>
{
    private readonly IOperationsDbContext _db;
    public CreateTicketHandler(IOperationsDbContext db) => _db = db;

    public async Task<SupportTicketDetailDto> HandleAsync(CreateTicketCommand cmd, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var count = await _db.SupportTickets.CountAsync(t => t.BrandId == cmd.BrandId, ct);
        var ticket = new SupportTicket
        {
            Id = Guid.NewGuid(),
            BrandId = cmd.BrandId,
            TicketNumber = $"TKT-{now.Year}-{cmd.BrandId.ToString()[..4].ToUpper()}-{(count + 1):D6}",
            RequesterType = cmd.RequesterType,
            RequesterId = cmd.RequesterId,
            CustomerId = cmd.CustomerId,
            RiderId = cmd.RiderId,
            OrderId = cmd.Request.OrderId,
            Subject = cmd.Request.Subject.Trim(),
            Category = string.IsNullOrWhiteSpace(cmd.Request.Category) ? "general" : cmd.Request.Category.Trim(),
            Priority = "normal",
            Status = "open",
            LastMessageAt = now,
            Metadata = "{}",
            CreatedAt = now, UpdatedAt = now, CreatedBy = cmd.RequesterId, UpdatedBy = cmd.RequesterId,
        };
        var msg = new TicketMessage
        {
            Id = Guid.NewGuid(), TicketId = ticket.Id, BrandId = cmd.BrandId,
            SenderType = cmd.RequesterType, SenderId = cmd.RequesterId,
            Body = cmd.Request.Message.Trim(), Metadata = "{}", CreatedAt = now, CreatedBy = cmd.RequesterId,
        };
        _db.SupportTickets.Add(ticket);
        _db.TicketMessages.Add(msg);
        await _db.SaveChangesAsync(ct);
        return new SupportTicketDetailDto(SupportMap.ToDto(ticket), [SupportMap.ToDto(msg)]);
    }
}

public sealed class CreateTicketValidator : AbstractValidator<CreateTicketCommand>
{
    public CreateTicketValidator()
    {
        RuleFor(x => x.Request.Subject).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.Message).NotEmpty().MaximumLength(4000);
    }
}

// ── List (requester self) ────────────────────────────────────────────────────

public sealed record GetMyTicketsQuery(Guid BrandId, Guid RequesterId) : IQuery<IReadOnlyList<SupportTicketDto>>;

public sealed class GetMyTicketsHandler : IQueryHandler<GetMyTicketsQuery, IReadOnlyList<SupportTicketDto>>
{
    private readonly IOperationsDbContext _db;
    public GetMyTicketsHandler(IOperationsDbContext db) => _db = db;

    public async Task<IReadOnlyList<SupportTicketDto>> HandleAsync(GetMyTicketsQuery q, CancellationToken ct)
        => await _db.SupportTickets.AsNoTracking()
            .Where(t => t.BrandId == q.BrandId && t.RequesterId == q.RequesterId)
            .OrderByDescending(t => t.LastMessageAt)
            .Select(t => new SupportTicketDto(t.Id, t.TicketNumber, t.RequesterType, null, t.Subject,
                t.Category, t.Priority, t.Status, t.OrderId, t.LastMessageAt, t.CreatedAt))
            .ToListAsync(ct);
}

// ── Detail (requester self OR admin) ─────────────────────────────────────────

public sealed record GetTicketDetailQuery(Guid TicketId, Guid? RequesterId, bool IsAdmin)
    : IQuery<SupportTicketDetailDto?>;

public sealed class GetTicketDetailHandler : IQueryHandler<GetTicketDetailQuery, SupportTicketDetailDto?>
{
    private readonly IOperationsDbContext _db;
    public GetTicketDetailHandler(IOperationsDbContext db) => _db = db;

    public async Task<SupportTicketDetailDto?> HandleAsync(GetTicketDetailQuery q, CancellationToken ct)
    {
        var t = await _db.SupportTickets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == q.TicketId, ct);
        if (t is null) return null;
        if (!q.IsAdmin && t.RequesterId != q.RequesterId) return null;   // IDOR guard

        var msgs = await _db.TicketMessages.AsNoTracking()
            .Where(m => m.TicketId == t.Id).OrderBy(m => m.CreatedAt)
            .Select(m => new TicketMessageDto(m.Id, m.SenderType, m.SenderId, m.Body, m.CreatedAt))
            .ToListAsync(ct);
        return new SupportTicketDetailDto(SupportMap.ToDto(t), msgs);
    }
}

// ── Post a message (requester or agent) ──────────────────────────────────────

public sealed record PostTicketMessageCommand(
    Guid TicketId, string SenderType, Guid? SenderId, string Body, bool IsAdmin, Guid? RequesterId)
    : ICommand<TicketMessageDto?>;

public sealed class PostTicketMessageHandler : ICommandHandler<PostTicketMessageCommand, TicketMessageDto?>
{
    private readonly IOperationsDbContext _db;
    public PostTicketMessageHandler(IOperationsDbContext db) => _db = db;

    public async Task<TicketMessageDto?> HandleAsync(PostTicketMessageCommand cmd, CancellationToken ct)
    {
        var t = await _db.SupportTickets.FirstOrDefaultAsync(x => x.Id == cmd.TicketId, ct);
        if (t is null) return null;
        if (!cmd.IsAdmin && t.RequesterId != cmd.RequesterId) return null;   // IDOR guard
        if (t.Status == "closed") return null;

        var now = DateTimeOffset.UtcNow;
        var msg = new TicketMessage
        {
            Id = Guid.NewGuid(), TicketId = t.Id, BrandId = t.BrandId,
            SenderType = cmd.SenderType, SenderId = cmd.SenderId,
            Body = cmd.Body.Trim(), Metadata = "{}", CreatedAt = now, CreatedBy = cmd.SenderId,
        };
        _db.TicketMessages.Add(msg);

        t.LastMessageAt = now;
        t.UpdatedAt = now;
        // An agent reply moves an open ticket into progress.
        if (cmd.IsAdmin && t.Status == "open") t.Status = "in_progress";
        await _db.SaveChangesAsync(ct);
        return SupportMap.ToDto(msg);
    }
}

public sealed class PostTicketMessageValidator : AbstractValidator<PostTicketMessageCommand>
{
    public PostTicketMessageValidator() => RuleFor(x => x.Body).NotEmpty().MaximumLength(4000);
}

// ── Admin: inbox + update ────────────────────────────────────────────────────

public sealed record GetTicketsInboxQuery(Guid BrandId, string? Status) : IQuery<IReadOnlyList<SupportTicketDto>>;

public sealed class GetTicketsInboxHandler : IQueryHandler<GetTicketsInboxQuery, IReadOnlyList<SupportTicketDto>>
{
    private readonly IOperationsDbContext _db;
    public GetTicketsInboxHandler(IOperationsDbContext db) => _db = db;

    public async Task<IReadOnlyList<SupportTicketDto>> HandleAsync(GetTicketsInboxQuery q, CancellationToken ct)
        => await _db.SupportTickets.AsNoTracking()
            .Where(t => t.BrandId == q.BrandId && (q.Status == null || t.Status == q.Status))
            .OrderByDescending(t => t.LastMessageAt)
            .Select(t => new SupportTicketDto(t.Id, t.TicketNumber, t.RequesterType, null, t.Subject,
                t.Category, t.Priority, t.Status, t.OrderId, t.LastMessageAt, t.CreatedAt))
            .ToListAsync(ct);
}

public sealed record UpdateTicketCommand(Guid TicketId, Guid BrandId, UpdateTicketRequest Request, Guid? ActorId)
    : ICommand<SupportTicketDto?>;

public sealed class UpdateTicketHandler : ICommandHandler<UpdateTicketCommand, SupportTicketDto?>
{
    private static readonly string[] Statuses = ["open", "in_progress", "resolved", "closed"];
    private static readonly string[] Priorities = ["low", "normal", "high"];
    private readonly IOperationsDbContext _db;
    public UpdateTicketHandler(IOperationsDbContext db) => _db = db;

    public async Task<SupportTicketDto?> HandleAsync(UpdateTicketCommand cmd, CancellationToken ct)
    {
        var t = await _db.SupportTickets.FirstOrDefaultAsync(x => x.Id == cmd.TicketId && x.BrandId == cmd.BrandId, ct);
        if (t is null) return null;

        if (cmd.Request.Status is { } s && Statuses.Contains(s)) t.Status = s;
        if (cmd.Request.Priority is { } p && Priorities.Contains(p)) t.Priority = p;
        if (cmd.Request.AssignedTo is { } a) t.AssignedTo = a;
        t.UpdatedAt = DateTimeOffset.UtcNow;
        t.UpdatedBy = cmd.ActorId;
        await _db.SaveChangesAsync(ct);
        return SupportMap.ToDto(t);
    }
}

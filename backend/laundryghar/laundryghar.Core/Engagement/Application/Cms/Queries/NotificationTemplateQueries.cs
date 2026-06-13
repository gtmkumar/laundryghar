using laundryghar.Engagement.Application.Cms.Commands;
using laundryghar.Engagement.Application.Cms.Dtos;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Engagement.Application.Cms.Queries;

// ── List ───────────────────────────────────────────────────────────────────────

public sealed record GetNotificationTemplatesQuery(int Page, int PageSize) : IRequest<PaginatedList<NotificationTemplateDto>>;

public sealed class GetNotificationTemplatesHandler
    : IRequestHandler<GetNotificationTemplatesQuery, PaginatedList<NotificationTemplateDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetNotificationTemplatesHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<PaginatedList<NotificationTemplateDto>> Handle(
        GetNotificationTemplatesQuery query, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var q = _db.NotificationTemplates
            .Where(x => x.BrandId == brandId)
            .OrderBy(x => x.Code)
            .Select(x => CreateNotificationTemplateHandler.ToDto(x));

        return await PaginatedList<NotificationTemplateDto>.CreateAsync(q, query.Page, query.PageSize, ct);
    }
}

// ── Get by Id ──────────────────────────────────────────────────────────────────

public sealed record GetNotificationTemplateByIdQuery(Guid Id) : IRequest<NotificationTemplateDto?>;

public sealed class GetNotificationTemplateByIdHandler
    : IRequestHandler<GetNotificationTemplateByIdQuery, NotificationTemplateDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetNotificationTemplateByIdHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<NotificationTemplateDto?> Handle(
        GetNotificationTemplateByIdQuery query, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var entity = await _db.NotificationTemplates
            .FirstOrDefaultAsync(x => x.Id == query.Id && x.BrandId == brandId, ct);
        return entity is null ? null : CreateNotificationTemplateHandler.ToDto(entity);
    }
}

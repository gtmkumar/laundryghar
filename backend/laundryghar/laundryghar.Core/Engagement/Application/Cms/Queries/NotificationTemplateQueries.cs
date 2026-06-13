using laundryghar.Engagement.Infrastructure.Services;
using ICurrentUser = laundryghar.Engagement.Infrastructure.Services.ICurrentUser;
using laundryghar.Engagement.Application.Cms.Commands;
using laundryghar.Engagement.Application.Cms.Dtos;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Engagement.Application.Cms.Queries;

// ── List ───────────────────────────────────────────────────────────────────────

/// <param name="Status">
/// Optional status filter. When null, archived templates are EXCLUDED from the
/// default list (soft-deleted templates must not reappear). Pass a specific status
/// (e.g. "archived") to return only those, or "all" to include every status.
/// </param>
public sealed record GetNotificationTemplatesQuery(int Page, int PageSize, string? Status = null)
    : IRequest<PaginatedList<NotificationTemplateDto>>;

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
        var baseQuery = _db.NotificationTemplates.Where(x => x.BrandId == brandId);

        // Status filter (see NotificationTemplateListFilter for the canonical rules,
        // which the unit tests assert against — keep these two in lock-step):
        //   null/empty → exclude archived (default — soft-deleted stay hidden)
        //   "all"      → no status filter (include archived)
        //   <specific> → exact match (e.g. "archived", "active")
        var status = query.Status?.Trim();
        if (string.IsNullOrEmpty(status))
            baseQuery = baseQuery.Where(x => x.Status != NotificationTemplateListFilter.ArchivedStatus);
        else if (!string.Equals(status, NotificationTemplateListFilter.AllSentinel, StringComparison.OrdinalIgnoreCase))
            baseQuery = baseQuery.Where(x => x.Status == status);

        var q = baseQuery
            .OrderBy(x => x.Code)
            .Select(x => CreateNotificationTemplateHandler.ToDto(x));

        return await PaginatedList<NotificationTemplateDto>.CreateAsync(q, query.Page, query.PageSize, ct);
    }
}

/// <summary>
/// Canonical rules for which notification templates appear in the admin list given an
/// optional <c>status</c> query param. Extracted as a pure function so the behaviour
/// (DEF-5: archived templates must not reappear in the default list) is unit-testable
/// without a database. <see cref="GetNotificationTemplatesHandler"/> applies the same
/// rules as an EF predicate.
/// </summary>
public static class NotificationTemplateListFilter
{
    public const string ArchivedStatus = "archived";
    public const string AllSentinel     = "all";

    /// <summary>
    /// Returns true if a template with <paramref name="templateStatus"/> should be
    /// included for the given (possibly null) <paramref name="filter"/>.
    /// </summary>
    public static bool ShouldInclude(string? filter, string templateStatus)
    {
        var status = filter?.Trim();
        if (string.IsNullOrEmpty(status))
            return !string.Equals(templateStatus, ArchivedStatus, StringComparison.Ordinal);
        if (string.Equals(status, AllSentinel, StringComparison.OrdinalIgnoreCase))
            return true;
        return string.Equals(templateStatus, status, StringComparison.Ordinal);
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

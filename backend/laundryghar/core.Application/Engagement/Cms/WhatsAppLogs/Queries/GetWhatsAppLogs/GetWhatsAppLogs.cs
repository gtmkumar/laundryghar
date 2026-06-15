using core.Application.Common.Interfaces;
using core.Application.Engagement.Cms.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Engagement.Cms.WhatsAppLogs.Queries.GetWhatsAppLogs;

// Admin list (paged), scoped to the caller's brand. Read-only WhatsApp message log.
public sealed record GetWhatsAppLogsQuery(int Page, int PageSize, string? Direction)
    : IQuery<PaginatedList<WhatsAppMessageLogDto>>;

public class GetWhatsAppLogsQueryHandler
    : IQueryHandler<GetWhatsAppLogsQuery, PaginatedList<WhatsAppMessageLogDto>>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;

    public GetWhatsAppLogsQueryHandler(ICoreDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<PaginatedList<WhatsAppMessageLogDto>> HandleAsync(
        GetWhatsAppLogsQuery query, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();

        var q = _db.WhatsAppMessageLogs.AsNoTracking().Where(x => x.BrandId == brandId);
        if (!string.IsNullOrEmpty(query.Direction))
            q = q.Where(x => x.Direction == query.Direction);

        var projected = q.OrderByDescending(x => x.SentAt)
            .Select(x => new WhatsAppMessageLogDto(
                x.Id, x.BrandId, x.Direction, x.CustomerId, x.UserId,
                x.PhoneE164, x.Provider, x.WaMessageId, x.WaConversationId,
                x.TemplateName, x.MessageType, x.BodyText,
                x.ReferenceType, x.ReferenceId, x.Status,
                x.SentAt, x.DeliveredAt, x.ReadAt,
                x.FailedAt, x.ErrorCode, x.ErrorMessage,
                x.CreatedAt));

        return await PaginatedList<WhatsAppMessageLogDto>.CreateAsync(
            projected, query.Page, query.PageSize, cancellationToken);
    }
}

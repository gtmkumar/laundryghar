using core.Application.Common.Interfaces;
using core.Application.Identity.Impersonation.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Impersonation.Queries;

public sealed record GetImpersonationGrantsQuery(Guid? BrandId)
    : IQuery<IReadOnlyList<ImpersonationGrantDto>>;

/// <summary>
/// The provider's console: every request ever made against this account and what happened to it.
/// Brand-scoped by RLS, so an owner sees their own and nothing else.
///
/// <para>Denied and revoked rows are included, not filtered out. A console that showed only live
/// sessions would answer "is anyone in my account right now" but not "has anyone ever been", and the
/// second question is the one people ask after something looks wrong.</para>
/// </summary>
public sealed class GetImpersonationGrantsQueryHandler
    : IQueryHandler<GetImpersonationGrantsQuery, IReadOnlyList<ImpersonationGrantDto>>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentTenant _tenant;

    public GetImpersonationGrantsQueryHandler(ICoreDbContext db, ICurrentTenant tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<ImpersonationGrantDto>> HandleAsync(
        GetImpersonationGrantsQuery query, CancellationToken ct)
    {
        var brandId = query.BrandId ?? _tenant.BrandId;

        var rows = await (
            from g in _db.ImpersonationGrants.AsNoTracking()
            where brandId == null || g.BrandId == brandId
            join u in _db.Users.AsNoTracking() on g.SupportUserId equals u.Id into su
            from u in su.DefaultIfEmpty()
            orderby g.RequestedAt descending
            select new ImpersonationGrantDto(
                g.Id, g.BrandId, g.SupportUserId,
                u == null ? null : (u.Email ?? u.PhoneE164),
                g.Reason, g.Scope,
                // Report expiry the same way the oracle does, so the console never shows a session
                // as "approved" minutes after it stopped working.
                g.Status == "approved" && g.ExpiresAt != null && g.ExpiresAt <= DateTimeOffset.UtcNow
                    ? "expired" : g.Status,
                g.RequestedAt, g.ApprovedByUserId, g.ApprovedAt, g.ExpiresAt,
                g.RevokedAt, g.RevokeReason))
            .ToListAsync(ct);

        return rows;
    }
}

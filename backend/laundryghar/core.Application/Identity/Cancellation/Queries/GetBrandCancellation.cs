using core.Application.Common.Interfaces;
using core.Application.Identity.Cancellation.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.TenancyOrg;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Cancellation.Queries;

public sealed record GetBrandCancellationQuery(Guid BrandId) : IQuery<BrandCancellationDto?>;

/// <summary>The current wind-down, or the most recent one if none is live — so an owner who
/// withdrew last month can still see that it happened.</summary>
public sealed class GetBrandCancellationQueryHandler
    : IQueryHandler<GetBrandCancellationQuery, BrandCancellationDto?>
{
    private readonly ICoreDbContext _db;
    public GetBrandCancellationQueryHandler(ICoreDbContext db) => _db = db;

    public async Task<BrandCancellationDto?> HandleAsync(
        GetBrandCancellationQuery query, CancellationToken ct)
    {
        var row = await _db.BrandCancellations.AsNoTracking()
            .Where(c => c.BrandId == query.BrandId)
            // A live window first, then the newest of whatever else there is.
            .OrderByDescending(c => c.Status == BrandCancellationStatus.Retention)
            .ThenByDescending(c => c.RequestedAt)
            .FirstOrDefaultAsync(ct);

        if (row is null) return null;

        var remaining = row.Status == BrandCancellationStatus.Retention
            ? (int)Math.Max(0, Math.Ceiling((row.RetentionUntil - DateTimeOffset.UtcNow).TotalDays))
            : 0;

        return new BrandCancellationDto(
            row.Id, row.BrandId, row.Status, row.Reason, row.RequestedAt, row.RetentionUntil,
            remaining, row.ExportCount, row.LastExportedAt, row.WithdrawnAt, row.PurgedAt);
    }
}

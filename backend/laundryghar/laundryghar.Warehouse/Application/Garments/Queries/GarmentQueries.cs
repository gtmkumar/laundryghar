using laundryghar.Warehouse.Application.Garments.Commands;
using laundryghar.Warehouse.Application.Garments.Dtos;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Warehouse.Application.Garments.Queries;

public sealed record GetGarmentsQuery(
    int Page, int PageSize,
    string? Stage, Guid? StoreId, Guid? BatchId
) : IRequest<PaginatedList<GarmentDto>>;

public sealed class GetGarmentsHandler : IRequestHandler<GetGarmentsQuery, PaginatedList<GarmentDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;
    public GetGarmentsHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<GarmentDto>> Handle(GetGarmentsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        // Defense-in-depth brand predicate (superuser bypasses RLS)
        var query = _db.Garments.Where(g => g.BrandId == brandId);

        if (!string.IsNullOrEmpty(q.Stage)) query = query.Where(g => g.CurrentStage == q.Stage);
        if (q.StoreId.HasValue)             query = query.Where(g => g.StoreId == q.StoreId.Value);
        if (q.BatchId.HasValue)             query = query.Where(g => g.CurrentBatchId == q.BatchId.Value);

        return PaginatedList<GarmentDto>.CreateAsync(
            query.OrderByDescending(g => g.CreatedAt).Select(g => CreateGarmentHandler.ToDto(g)),
            q.Page, q.PageSize, ct);
    }
}

public sealed record GetGarmentByIdQuery(Guid Id) : IRequest<GarmentDto?>;

public sealed class GetGarmentByIdHandler : IRequestHandler<GetGarmentByIdQuery, GarmentDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;
    public GetGarmentByIdHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<GarmentDto?> Handle(GetGarmentByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var g = await _db.Garments
            .FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId, ct);
        return g is null ? null : CreateGarmentHandler.ToDto(g);
    }
}

// ── Garment by tag ─────────────────────────────────────────────────────────────

public sealed record GetGarmentByTagQuery(string TagCode) : IRequest<GarmentJourneyDto?>;

public sealed class GetGarmentByTagHandler : IRequestHandler<GetGarmentByTagQuery, GarmentJourneyDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;
    public GetGarmentByTagHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<GarmentJourneyDto?> Handle(GetGarmentByTagQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();

        var garment = await _db.Garments
            .FirstOrDefaultAsync(g => g.TagCode == q.TagCode && g.BrandId == brandId, ct);
        if (garment is null) return null;

        // Inspections
        var inspections = await _db.GarmentInspections
            .Where(i => i.GarmentId == garment.Id && i.BrandId == brandId)
            .OrderBy(i => i.InspectedAt)
            .Select(i => new InspectionSummaryDto(
                i.Id, i.InspectionType, i.OverallCondition,
                i.IssuesCount, i.RequiresSpecialCare, i.InspectedAt))
            .ToListAsync(ct);

        // Process logs (partitioned — brand-scoped via brand_id column)
        var logs = await _db.ProcessLogs
            .Where(pl => pl.GarmentId == garment.Id && pl.BrandId == brandId)
            .OrderBy(pl => pl.OccurredAt)
            .Select(pl => new ProcessLogDto(
                pl.Id, pl.ProcessCode, pl.Action, pl.FromStage, pl.ToStage, pl.OccurredAt))
            .ToListAsync(ct);

        // QC history
        var qcs = await _db.QualityChecks
            .Where(qc => qc.GarmentId == garment.Id && qc.BrandId == brandId)
            .OrderBy(qc => qc.InspectedAt)
            .Select(qc => new QcSummaryDto(
                qc.Id, qc.Result, qc.RequiresRewash, qc.QcRound, qc.InspectedAt))
            .ToListAsync(ct);

        return new GarmentJourneyDto(CreateGarmentHandler.ToDto(garment), inspections, logs, qcs);
    }
}

// ── Tags ──────────────────────────────────────────────────────────────────────

public sealed record GetTagsQuery(int Page, int PageSize, string? Status)
    : IRequest<PaginatedList<GarmentTagDto>>;

public sealed class GetTagsHandler : IRequestHandler<GetTagsQuery, PaginatedList<GarmentTagDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;
    public GetTagsHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<GarmentTagDto>> Handle(GetTagsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var query   = _db.GarmentTags.Where(t => t.BrandId == brandId);
        if (!string.IsNullOrEmpty(q.Status)) query = query.Where(t => t.Status == q.Status);

        return PaginatedList<GarmentTagDto>.CreateAsync(
            query.OrderByDescending(t => t.CreatedAt)
                .Select(t => new GarmentTagDto(
                    t.Id, t.BrandId, t.TagCode, t.TagFormat,
                    t.BatchNumber, t.AssignedToGarmentId,
                    t.AssignedAt, t.IsDamaged, t.Status, t.CreatedAt)),
            q.Page, q.PageSize, ct);
    }
}

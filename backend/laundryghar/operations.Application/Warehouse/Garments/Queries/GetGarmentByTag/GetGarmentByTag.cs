using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Warehouse.Garments.Commands.CreateGarment;
using operations.Application.Warehouse.Garments.Dtos;

namespace operations.Application.Warehouse.Garments.Queries.GetGarmentByTag;

// ── Garment by tag ─────────────────────────────────────────────────────────────

public sealed record GetGarmentByTagQuery(string TagCode) : IQuery<GarmentJourneyDto?>;

public class GetGarmentByTagQueryHandler : IQueryHandler<GetGarmentByTagQuery, GarmentJourneyDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public GetGarmentByTagQueryHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<GarmentJourneyDto?> HandleAsync(GetGarmentByTagQuery query, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();

        var garment = await _db.Garments
            .FirstOrDefaultAsync(g => g.TagCode == query.TagCode && g.BrandId == brandId, cancellationToken);
        if (garment is null) return null;

        // Inspections
        var inspections = await _db.GarmentInspections
            .Where(i => i.GarmentId == garment.Id && i.BrandId == brandId)
            .OrderBy(i => i.InspectedAt)
            .Select(i => new InspectionSummaryDto(
                i.Id, i.InspectionType, i.OverallCondition,
                i.IssuesCount, i.RequiresSpecialCare, i.InspectedAt))
            .ToListAsync(cancellationToken);

        // Process logs (partitioned — brand-scoped via brand_id column)
        var logs = await _db.ProcessLogs
            .Where(pl => pl.GarmentId == garment.Id && pl.BrandId == brandId)
            .OrderBy(pl => pl.OccurredAt)
            .Select(pl => new ProcessLogDto(
                pl.Id, pl.ProcessCode, pl.Action, pl.FromStage, pl.ToStage, pl.OccurredAt))
            .ToListAsync(cancellationToken);

        // QC history
        var qcs = await _db.QualityChecks
            .Where(qc => qc.GarmentId == garment.Id && qc.BrandId == brandId)
            .OrderBy(qc => qc.InspectedAt)
            .Select(qc => new QcSummaryDto(
                qc.Id, qc.Result, qc.RequiresRewash, qc.QcRound, qc.InspectedAt))
            .ToListAsync(cancellationToken);

        return new GarmentJourneyDto(CreateGarmentCommandHandler.ToDto(garment), inspections, logs, qcs);
    }
}

using System.Text.Json;
using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using operations.Application.Common.Interfaces;

namespace operations.Application.Catalog.Pricing.Common;

/// <summary>Adds a pricing audit entry to the change log (saved in the caller's transaction).
/// Before/after are serialized snapshots used by the Change history tab and Revert.</summary>
public static class PricingChangeLogger
{
    public static void Add(IOperationsDbContext db, Guid brandId, string targetKind, Guid targetId,
        string summary, object before, object after, Guid? actorId, string? actorName)
    {
        db.PricingChangeLogs.Add(new PricingChangeLog
        {
            Id = Guid.NewGuid(),
            BrandId = brandId,
            TargetKind = targetKind,
            TargetId = targetId,
            Summary = summary,
            BeforeJson = JsonSerializer.Serialize(before),
            AfterJson = JsonSerializer.Serialize(after),
            ActorId = actorId,
            ActorName = actorName,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }
}

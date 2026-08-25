using core.Application.Common.Interfaces;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.TenancyOrg;
using laundryghar.Utilities.Auth.Audit;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Cancellation.Commands;

public sealed record RecordBrandExportCommand(Guid BrandId, long RecordCount) : ICommand<bool>;

/// <summary>
/// Records that an export completed. Two reasons this is not just a counter:
///
/// <list type="number">
/// <item>§9 says cancellation includes "export offered". This is the evidence it happened, and an
/// owner mid-wind-down can see it before the window closes.</item>
/// <item>A full tenant export is a bulk read of everything a company has. That belongs in the audit
/// ledger whether or not anyone is cancelling — the interceptor cannot see it, because a read
/// changes nothing for it to intercept.</item>
/// </list>
/// </summary>
public sealed class RecordBrandExportCommandHandler : ICommandHandler<RecordBrandExportCommand, bool>
{
    private readonly ICoreDbContext _db;
    private readonly IAuditWriter _audit;

    public RecordBrandExportCommandHandler(ICoreDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<bool> HandleAsync(RecordBrandExportCommand cmd, CancellationToken ct)
    {
        var live = await _db.BrandCancellations.FirstOrDefaultAsync(
            c => c.BrandId == cmd.BrandId && c.Status == BrandCancellationStatus.Retention, ct);

        if (live is not null)
        {
            live.ExportCount += 1;
            live.LastExportedAt = DateTimeOffset.UtcNow;
            live.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        await _audit.WriteAsync("brand.exported", "brand", cmd.BrandId,
            newValues: new { records = cmd.RecordCount }, ct: ct);

        return true;
    }
}

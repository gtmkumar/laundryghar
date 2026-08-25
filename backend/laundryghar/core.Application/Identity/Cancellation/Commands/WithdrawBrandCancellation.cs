using core.Application.Common.Interfaces;
using core.Application.Identity.Cancellation.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.SharedDataModel.Entities.TenancyOrg;
using laundryghar.Utilities.Auth.Audit;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Cancellation.Commands;

public sealed record WithdrawBrandCancellationCommand(
    Guid BrandId, WithdrawBrandCancellationRequest Request, Guid? ActorId) : ICommand<bool>;

/// <summary>
/// Changes their mind. Allowed at any point before the purge, because people cancel by accident and
/// the cost of allowing a reversal is one boolean while the cost of refusing one is a business.
///
/// <para>Restores <c>active</c> rather than whatever the brand was before. The only prior state that
/// could be lost is <c>suspended</c>, and a brand that owes money will be re-suspended by the billing
/// job on its next run — whereas leaving a paying customer suspended after they withdrew a
/// cancellation is an outage we caused.</para>
/// </summary>
public sealed class WithdrawBrandCancellationCommandHandler
    : ICommandHandler<WithdrawBrandCancellationCommand, bool>
{
    private readonly ICoreDbContext _db;
    private readonly IBrandStatusStore _brands;
    private readonly IAuditWriter _audit;

    public WithdrawBrandCancellationCommandHandler(
        ICoreDbContext db, IBrandStatusStore brands, IAuditWriter audit)
    {
        _db = db;
        _brands = brands;
        _audit = audit;
    }

    public async Task<bool> HandleAsync(WithdrawBrandCancellationCommand cmd, CancellationToken ct)
    {
        var live = await _db.BrandCancellations.FirstOrDefaultAsync(
            c => c.BrandId == cmd.BrandId && c.Status == BrandCancellationStatus.Retention, ct);
        if (live is null) return false;

        var now = DateTimeOffset.UtcNow;
        live.Status = BrandCancellationStatus.Withdrawn;
        live.WithdrawnAt = now;
        live.WithdrawnByUserId = cmd.ActorId;
        live.UpdatedAt = now;
        live.UpdatedBy = cmd.ActorId;

        await _db.SaveChangesAsync(ct);

        // Unfreeze only after the record says withdrawn, so a failure here leaves a frozen brand
        // whose wind-down is already cancelled — recoverable by retrying — rather than a live brand
        // still marked for deletion.
        await _brands.SetCancellationStateAsync(cmd.BrandId, "active", ct);

        await _audit.WriteAsync("brand.cancellation_withdrawn", "brand", cmd.BrandId,
            newValues: new { reason = cmd.Request.Reason }, ct: ct);

        return true;
    }
}

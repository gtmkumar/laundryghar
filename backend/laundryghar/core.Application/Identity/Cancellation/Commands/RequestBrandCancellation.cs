using core.Application.Common.Interfaces;
using core.Application.Identity.Cancellation.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.SharedDataModel.Entities.TenancyOrg;
using laundryghar.Utilities.Auth.Audit;
using laundryghar.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Cancellation.Commands;

public sealed record RequestBrandCancellationCommand(
    Guid BrandId, RequestBrandCancellationRequest Request, Guid? ActorId) : ICommand<Guid?>;

/// <summary>
/// Starts a wind-down (§9). Moves the brand to <c>cancelled</c> — operations frozen, login/billing/
/// export still open — and opens the retention window.
///
/// <para>Nothing is deleted here, and nothing is deleted by any request path: the purge belongs to
/// the worker, on a clock the retention window sets. That separation is the point. A cancellation
/// endpoint that deletes is one mis-click from being unrecoverable, and §9's own word for this state
/// is "wind-down", not "delete".</para>
/// </summary>
public sealed class RequestBrandCancellationCommandHandler
    : ICommandHandler<RequestBrandCancellationCommand, Guid?>
{
    /// <summary>Default window. Long enough that an owner who cancels on a Friday still has their
    /// data when they think better of it, short enough that we are not storing a dead tenant for a
    /// year.</summary>
    public const int DefaultRetentionDays = 30;

    /// <summary>Floor. A window shorter than this makes "export offered" theatre — nobody exports a
    /// business over a weekend.</summary>
    public const int MinRetentionDays = 7;

    /// <summary>Ceiling. Past this it is not retention, it is indefinite storage of data a customer
    /// asked us to delete — which is the thing DPDP exists to stop.</summary>
    public const int MaxRetentionDays = 180;

    private readonly ICoreDbContext _db;
    private readonly IBrandStatusStore _brands;
    private readonly IAuditWriter _audit;

    public RequestBrandCancellationCommandHandler(
        ICoreDbContext db, IBrandStatusStore brands, IAuditWriter audit)
    {
        _db = db;
        _brands = brands;
        _audit = audit;
    }

    public async Task<Guid?> HandleAsync(RequestBrandCancellationCommand cmd, CancellationToken ct)
    {
        var days = cmd.Request.RetentionDays ?? DefaultRetentionDays;
        if (days < MinRetentionDays || days > MaxRetentionDays)
            throw new ValidationException(new Dictionary<string, string[]>
            { ["retentionDays"] = [$"Choose between {MinRetentionDays} and {MaxRetentionDays} days."] });

        // The brand is read and written through IBrandStatusStore, never EF: tenancy_org.brands is
        // admin-only under RLS, so an owner cancelling their own account reads zero rows from it.
        var currentStatus = await _brands.GetStatusAsync(cmd.BrandId, ct);
        if (currentStatus is null) return null;

        if (currentStatus == "archived")
            throw new ValidationException(new Dictionary<string, string[]>
            { ["status"] = ["This account has already been closed and its data deleted."] });

        var live = await _db.BrandCancellations.FirstOrDefaultAsync(
            c => c.BrandId == cmd.BrandId && c.Status == BrandCancellationStatus.Retention, ct);

        // Already winding down — return the existing record rather than starting a second window.
        // Re-requesting must never RESET the clock: that would let a mistake, or a loop, keep a
        // tenant's data alive indefinitely under the appearance of being deleted.
        if (live is not null) return live.Id;

        var now = DateTimeOffset.UtcNow;
        var row = new BrandCancellation
        {
            Id = Guid.NewGuid(),
            BrandId = cmd.BrandId,
            RequestedByUserId = cmd.ActorId,
            Reason = cmd.Request.Reason,
            RequestedAt = now,
            RetentionUntil = now.AddDays(days),
            Status = BrandCancellationStatus.Retention,
            CreatedAt = now, UpdatedAt = now, CreatedBy = cmd.ActorId, UpdatedBy = cmd.ActorId,
        };
        _db.BrandCancellations.Add(row);
        await _db.SaveChangesAsync(ct);

        // Freeze operations LAST. If the status flip succeeded and the record write then failed, the
        // brand would be frozen with no wind-down row explaining why and no way to withdraw — a
        // tenant locked out by a half-finished cancellation.
        await _brands.SetCancellationStateAsync(cmd.BrandId, "cancelled", ct);

        await _audit.WriteAsync("brand.cancellation_requested", "brand", cmd.BrandId,
            newValues: new { retentionUntil = row.RetentionUntil, reason = cmd.Request.Reason },
            ct: ct);

        return row.Id;
    }
}

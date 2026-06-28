using core.Application.Common.Interfaces;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Entitlements.Commands;

/// <summary>Cancel a brand's platform subscription (stops renewals; drops it from MRR / active tenants).
/// The brand keeps any already-licensed features until an operator changes entitlement separately.
/// Idempotent; re-applying a bundle re-activates the subscription.</summary>
public sealed record CancelBrandPlatformSubscriptionCommand(Guid BrandId, Guid? ActorId) : ICommand<bool>;

public class CancelBrandPlatformSubscriptionCommandHandler
    : ICommandHandler<CancelBrandPlatformSubscriptionCommand, bool>
{
    private readonly ICoreDbContext _db;
    public CancelBrandPlatformSubscriptionCommandHandler(ICoreDbContext db) => _db = db;

    public async Task<bool> HandleAsync(CancelBrandPlatformSubscriptionCommand cmd, CancellationToken ct)
    {
        var sub = await _db.BrandPlatformSubscriptions.FirstOrDefaultAsync(s => s.BrandId == cmd.BrandId, ct);
        if (sub is null) return false;
        if (string.Equals(sub.Status, "cancelled", StringComparison.OrdinalIgnoreCase)) return true;

        sub.Status = "cancelled";
        sub.AutoRenew = false;
        sub.UpdatedAt = DateTimeOffset.UtcNow;
        sub.UpdatedBy = cmd.ActorId;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

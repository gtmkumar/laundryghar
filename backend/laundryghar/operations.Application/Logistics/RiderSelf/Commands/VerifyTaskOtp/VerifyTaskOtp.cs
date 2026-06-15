using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.Common;
using operations.Application.Logistics.RiderSelf.Dtos;

namespace operations.Application.Logistics.RiderSelf.Commands.VerifyTaskOtp;

// ── Verify delivery OTP (server-side; OTP never leaves the server) ─────────────

public sealed record VerifyTaskOtpCommand(
    Guid AssignmentId, Guid UserId, Guid BrandId, string Code) : ICommand<RiderTaskResult>;

public sealed class VerifyTaskOtpHandler : ICommandHandler<VerifyTaskOtpCommand, RiderTaskResult>
{
    private readonly IOperationsDbContext _db;
    public VerifyTaskOtpHandler(IOperationsDbContext db) => _db = db;

    public async Task<RiderTaskResult> HandleAsync(VerifyTaskOtpCommand command, CancellationToken cancellationToken)
    {
        var ct  = cancellationToken;
        var cmd = command;
        var rider = await _db.Riders
            .Where(r => r.UserId == cmd.UserId && r.BrandId == cmd.BrandId)
            .Select(r => new { r.Id }).FirstOrDefaultAsync(ct);
        if (rider is null) return RiderTaskResult.NotFound();

        var da = await _db.DeliveryAssignments
            .FirstOrDefaultAsync(x => x.Id == cmd.AssignmentId
                                   && x.RiderId == rider.Id
                                   && x.BrandId == cmd.BrandId, ct);
        if (da is null) return RiderTaskResult.NotFound();

        var o = (da.OrderId is not null && da.OrderCreatedAt is not null)
            ? await _db.Orders.FirstOrDefaultAsync(
                x => x.Id == da.OrderId && x.CreatedAt == da.OrderCreatedAt, ct)
            : null;

        var isDelivery = da.LegType is "delivery" or "return";
        var expected = isDelivery ? o?.DeliveryOtp : o?.PickupOtp;

        var now = DateTimeOffset.UtcNow;
        da.OtpAttemptedAt = now;
        da.UpdatedAt = now;

        var supplied = (cmd.Code ?? string.Empty).Trim();
        var ok = !string.IsNullOrWhiteSpace(expected)
                 && string.Equals(expected.Trim(), supplied, StringComparison.Ordinal);

        if (ok)
        {
            da.OtpVerified = true;
            // A verified pickup OTP IS the collection handshake — stamp collected_at
            // so the geofence can begin watching for the drop at the store.
            if (da.LegType == "pickup") da.CollectedAt ??= now;
        }
        await _db.SaveChangesAsync(ct);

        if (!ok) return RiderTaskResult.Conflict("Incorrect OTP.");

        var c = o is not null ? await _db.Customers.FirstOrDefaultAsync(x => x.Id == o.CustomerId, ct) : null;
        var addrId = o is null ? (Guid?)null : (da.LegType == "pickup" ? o.PickupAddressId : o.DeliveryAddressId);
        var addr = addrId.HasValue
            ? await _db.CustomerAddresses.FirstOrDefaultAsync(a => a.Id == addrId.Value, ct)
            : null;

        var payoutCfg = await PayoutConfig.LoadAsync(_db, cmd.BrandId, ct);
        return RiderTaskResult.Ok(RiderTaskMapper.ToDto(da, o, c, addr, payoutCfg));
    }
}

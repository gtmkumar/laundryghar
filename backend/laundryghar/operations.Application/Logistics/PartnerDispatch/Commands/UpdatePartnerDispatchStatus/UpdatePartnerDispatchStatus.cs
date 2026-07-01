using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;
using laundryghar.Utilities.Exceptions;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.PartnerDispatch.Common;
using operations.Application.Logistics.PartnerDispatch.Dtos;

namespace operations.Application.Logistics.PartnerDispatch.Commands.UpdatePartnerDispatchStatus;

/// <param name="DispatchId">The dispatch to advance.</param>
/// <param name="Request">The status/OTP/proof/location mutation(s).</param>
/// <param name="ActorId">The brand-staff user performing the update (audit updated_by).</param>
public sealed record UpdatePartnerDispatchStatusCommand(
    Guid DispatchId, UpdatePartnerDispatchStatusRequest Request, Guid? ActorId)
    : ICommand<PartnerDispatchDto?>;

/// <summary>
/// Staff/fleet path: advance a dispatch — change status (validated against the state machine),
/// verify pickup/drop OTP, attach proof, and/or push the rider's last-known location. Runs in a
/// BRAND-STAFF session; the <c>rls_partner_or_brand</c> policy grants visibility via the BRAND arm
/// (brand_id = current_brand_id), so a staff member can only load dispatches its own fleet serves —
/// an out-of-brand dispatch id is invisible and yields a null (→ 404). Returns null when the
/// dispatch is not found/not visible.
/// </summary>
public sealed class UpdatePartnerDispatchStatusHandler
    : ICommandHandler<UpdatePartnerDispatchStatusCommand, PartnerDispatchDto?>
{
    private readonly IOperationsDbContext _db;

    public UpdatePartnerDispatchStatusHandler(IOperationsDbContext db) => _db = db;

    public async Task<PartnerDispatchDto?> HandleAsync(
        UpdatePartnerDispatchStatusCommand cmd, CancellationToken ct)
    {
        var e = await _db.PartnerDispatches
            .FirstOrDefaultAsync(d => d.Id == cmd.DispatchId, ct);
        if (e is null) return null; // not found or outside this brand's fleet → 404

        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;
        var mutated = false;

        // ── Rider (re)assignment ─────────────────────────────────────────────
        if (req.RiderId is { } rid && rid != Guid.Empty && rid != e.RiderId)
        {
            e.RiderId = rid;
            e.AssignedAt ??= now;
            mutated = true;
        }

        // ── Status transition (state-machine guarded) ────────────────────────
        if (!string.IsNullOrWhiteSpace(req.Status) && req.Status != e.Status)
        {
            var to = req.Status.Trim();
            if (!PartnerDispatchMapper.IsKnownStatus(to))
                throw new BusinessRuleException($"Unknown dispatch status '{to}'.");
            if (!PartnerDispatchMapper.CanTransition(e.Status, to))
                throw new BusinessRuleException(
                    $"Illegal dispatch transition '{e.Status}' → '{to}'.");

            e.Status = to;
            if (to == PartnerDispatchMapper.Assigned) e.AssignedAt ??= now;
            mutated = true;
        }

        // ── OTP verification ─────────────────────────────────────────────────
        if (req.VerifyPickupOtp)
        {
            if (string.IsNullOrEmpty(e.PickupOtp) || req.PickupOtp != e.PickupOtp)
                throw new BusinessRuleException("Pickup OTP verification failed.");
            e.PickupVerifiedAt = now;
            mutated = true;
        }
        if (req.VerifyDropOtp)
        {
            if (string.IsNullOrEmpty(e.DropOtp) || req.DropOtp != e.DropOtp)
                throw new BusinessRuleException("Drop OTP verification failed.");
            e.DropVerifiedAt = now;
            mutated = true;
        }

        // ── Proof of delivery ────────────────────────────────────────────────
        if (req.ProofPhotoUrl is not null) { e.ProofPhotoUrl = req.ProofPhotoUrl; mutated = true; }
        if (req.ProofSignatureUrl is not null) { e.ProofSignatureUrl = req.ProofSignatureUrl; mutated = true; }

        // ── Last-known location ping ─────────────────────────────────────────
        if (req.LastKnownLat is { } lat && req.LastKnownLng is { } lng)
        {
            e.LastKnownLat = (decimal)lat;
            e.LastKnownLng = (decimal)lng;
            e.LastLocationAt = now;
            mutated = true;
        }

        if (mutated)
        {
            e.UpdatedAt = now;
            e.UpdatedBy = cmd.ActorId;
            await _db.SaveChangesAsync(ct);
        }

        return PartnerDispatchMapper.ToDto(e);
    }
}

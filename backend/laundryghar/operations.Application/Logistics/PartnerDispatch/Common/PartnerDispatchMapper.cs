using Entity = laundryghar.SharedDataModel.Entities.Logistics.PartnerDispatch;
using operations.Application.Logistics.PartnerDispatch.Dtos;

namespace operations.Application.Logistics.PartnerDispatch.Common;

/// <summary>Projection helpers + the dispatch status state machine. Kept in one place so the assign
/// and update handlers stay in agreement about legal transitions and DTO shape.</summary>
internal static class PartnerDispatchMapper
{
    // Canonical status vocabulary (mirrors the partner_dispatches CHECK constraint).
    public const string Pending       = "pending";
    public const string Assigned      = "assigned";
    public const string EnRoutePickup = "en_route_pickup";
    public const string PickedUp      = "picked_up";
    public const string EnRouteDrop   = "en_route_drop";
    public const string Delivered     = "delivered";
    public const string Cancelled     = "cancelled";

    /// <summary>Allowed forward transitions. 'cancelled' is reachable from any non-terminal state.
    /// Terminal states ('delivered','cancelled') have no outgoing transitions.</summary>
    private static readonly Dictionary<string, string[]> Transitions = new()
    {
        [Pending]       = [Assigned, Cancelled],
        [Assigned]      = [EnRoutePickup, Cancelled],
        [EnRoutePickup] = [PickedUp, Cancelled],
        [PickedUp]      = [EnRouteDrop, Cancelled],
        [EnRouteDrop]   = [Delivered, Cancelled],
        [Delivered]     = [],
        [Cancelled]     = [],
    };

    public static bool IsKnownStatus(string status) => Transitions.ContainsKey(status);

    public static bool CanTransition(string from, string to) =>
        Transitions.TryGetValue(from, out var next) && Array.IndexOf(next, to) >= 0;

    public static PartnerDispatchDto ToDto(Entity e) => new(
        e.Id,
        e.PartnerId,
        e.PartnerBookingId,
        e.BrandId,
        e.RiderId,
        e.Status,
        e.PickupVerifiedAt,
        e.DropVerifiedAt,
        e.ProofPhotoUrl,
        e.ProofSignatureUrl,
        e.LastKnownLat,
        e.LastKnownLng,
        e.LastLocationAt,
        e.AssignedAt,
        e.CreatedAt,
        e.UpdatedAt);

    public static PartnerBookingTrackDto ToTrackDto(Guid partnerBookingId, Entity? e) =>
        e is null
            ? new PartnerBookingTrackDto(partnerBookingId, false, null, "not_dispatched",
                null, null, null, null, null, null, null, null, null, null)
            : new PartnerBookingTrackDto(
                partnerBookingId,
                true,
                e.Id,
                e.Status,
                e.RiderId,
                e.LastKnownLat,
                e.LastKnownLng,
                e.LastLocationAt,
                e.PickupVerifiedAt,
                e.DropVerifiedAt,
                e.ProofPhotoUrl,
                e.ProofSignatureUrl,
                e.AssignedAt,
                e.UpdatedAt);
}

namespace operations.Application.Fulfillment.Salon;

/// <summary>
/// The salon <c>appointment</c> fulfilment vocabulary — a strategy-PRIVATE status set that lives
/// OUTSIDE the shared <c>OrderStatus</c> (a salon booking has no pickup/wash/QC stages). This is the
/// proof the multi-vertical seam (Phase 1) supports a strategy with its own status vocabulary: the
/// shared spine persists only the neutral <c>OrderLifecycleState</c> super-state, while these
/// detailed statuses are owned by <see cref="SalonAppointmentStrategy"/>. (Phase 4.)
/// </summary>
public static class SalonStatus
{
    public const string Booked    = "booked";
    public const string Confirmed = "confirmed";
    public const string CheckedIn = "checked_in";
    public const string InService = "in_service";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string NoShow    = "no_show";
}

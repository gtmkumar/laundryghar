using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using operations.Application.Logistics.RiderSelf.Dtos;

namespace operations.Application.Logistics.RiderSelf;

internal static class RiderTaskMapper
{
    /// <summary>Non-terminal statuses that are always shown in the active feed.</summary>
    internal static readonly string[] OpenStatuses =
        ["assigned", "accepted", "started", "arrived"];

    private static readonly TimeSpan Ist = TimeSpan.FromHours(5.5);

    /// <summary>Maps backend DeliveryAssignmentStatus → the slimmer status the app understands.</summary>
    private static string MapStatus(string s) => s switch
    {
        "accepted" => "assigned",   // employee riders skip an explicit accept step
        "rejected" => "cancelled",
        "rescheduled" => "assigned",
        _ => s,            // assigned/started/arrived/completed/failed/cancelled pass through
    };

    private static string BuildAddressLine(CustomerAddress? a)
    {
        if (a is null) return "Address on file";
        var parts = new[] { a.FlatNumber, a.BuildingName, a.AddressLine1, a.Area }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        var line = string.Join(", ", parts);
        return string.IsNullOrWhiteSpace(line) ? a.City : line;
    }

    private static string? BuildZone(CustomerAddress? a)
    {
        if (a is null) return null;
        var z = a.Area ?? a.City;
        return string.IsNullOrWhiteSpace(z) ? null : z;
    }

    private static string CustomerName(Customer? c, CustomerAddress? a)
    {
        if (!string.IsNullOrWhiteSpace(a?.RecipientName)) return a!.RecipientName!;
        if (!string.IsNullOrWhiteSpace(c?.DisplayName)) return c!.DisplayName!;
        var full = $"{c?.FirstName} {c?.LastName}".Trim();
        if (!string.IsNullOrWhiteSpace(full)) return full;
        return "Customer";
    }

    /// <summary>
    /// Fine-grained step within a leg, derived from the lifecycle timestamps. For a
    /// pickup this captures the two-part round-trip (to_customer → at_customer →
    /// to_store → dropped); delivery/return have no store drop.
    /// </summary>
    private static string Phase(DeliveryAssignment da)
    {
        var st = MapStatus(da.Status);
        if (st is "completed" or "failed" or "cancelled") return st;

        if (da.LegType == "pickup")
        {
            if (da.DroppedAt is not null) return "dropped";      // dropped at the laundry
            if (da.CollectedAt is not null) return "to_store";     // collected, heading to the store
            if (st == "arrived") return "at_customer";  // on site, awaiting collection
            return st == "started" ? "to_customer" : "assigned";
        }

        // delivery / return — single destination (the customer)
        if (st == "arrived") return "at_customer";
        return st == "started" ? "to_customer" : "assigned";
    }

    /// <param name="pr">
    /// DEFECT 4 — the source pickup request for a pickup leg. Pickup-leg assignments
    /// link to a PickupRequest (not an Order: order_id is null), so without this the
    /// order-derived fields all fell back to placeholders ("—", "Customer",
    /// "Address on file", 0, 0). When <paramref name="o"/> is null we read the
    /// customer-facing fields from <paramref name="pr"/> instead.
    /// </param>
    public static RiderTaskDto ToDto(
        DeliveryAssignment da, Order? o, Customer? c, CustomerAddress? addr,
        RiderPayoutSettings payout, PickupRequest? pr = null)
    {
        var isDelivery = da.LegType is "delivery" or "return";

        var isExpress = o?.IsExpress ?? pr?.IsExpress ?? false;
        // Amount due: the order balance for deliveries; the pickup request's estimated
        // amount for an un-converted pickup leg (what the rider collects as COD).
        var amountDue = o?.AmountDue ?? (o is null ? pr?.EstimatedAmount ?? 0m : 0m);

        // OTP applies to BOTH legs: the customer reads it out to confirm the handover —
        // collecting items at pickup AND receiving them at delivery.
        var legOtp = isDelivery ? o?.DeliveryOtp : o?.PickupOtp;
        var requiresOtp = !string.IsNullOrWhiteSpace(legOtp);

        // Scheduled time: order timestamp when available; else the pickup request's
        // scheduled window start (rendered in IST as "HH:mm").
        string? scheduled;
        var scheduledAt = isDelivery ? o?.PromisedDeliveryAt : o?.PickupScheduledAt;
        if (scheduledAt is not null)
            scheduled = scheduledAt.Value.ToOffset(Ist).ToString("HH:mm");
        else if (o is null && pr is not null)
            scheduled = pr.PickupWindowStart.ToString("HH:mm");
        else
            scheduled = null;

        // Prefer the assignment's own geo, else the address geo.
        var pt = da.GeoLocation ?? addr?.GeoLocation;
        double? lat = pt?.Y;
        double? lng = pt?.X;

        // Payout: the amount persisted at completion, else a live estimate from the
        // configured rates. COD bonus applies to a delivery that still has cash due.
        var hasCod = da.CodAmount is > 0m
                  || (isDelivery && amountDue > 0m && o?.PaymentStatus != "paid");
        var payoutAmt = da.PayoutAmount ?? payout.Compute(da.DistanceKm, isExpress, hasCod);

        var garmentCount = o?.TotalGarments ?? (o is null ? pr?.EstimatedItems ?? 0 : 0);
        var isPaid = (o?.PaymentStatus == "paid") || amountDue <= 0m;

        return new RiderTaskDto(
            Id: da.Id,
            OrderNumber: o?.OrderNumber ?? pr?.RequestNumber ?? "—",
            LegType: da.LegType,
            Status: MapStatus(da.Status),
            IsExpress: isExpress,
            CustomerName: CustomerName(c, addr),
            CustomerPhone: addr?.RecipientPhone ?? c?.PhoneE164,
            AddressLine: BuildAddressLine(addr),
            ZoneLabel: BuildZone(addr),
            DistanceKm: da.DistanceKm,
            EtaMinutes: da.DurationMinutes,
            ScheduledTime: scheduled,
            GarmentCount: garmentCount,
            AmountDue: amountDue,
            IsPaid: isPaid,
            RequiresOtp: requiresOtp,
            OtpVerified: da.OtpVerified,
            Payout: payoutAmt,
            Lat: lat,
            Lng: lng,
            SequenceNumber: da.SequenceNumber,
            CompletedAt: da.CompletedAt,
            CollectedAt: da.CollectedAt,
            DroppedAt: da.DroppedAt,
            Phase: Phase(da));
    }
}

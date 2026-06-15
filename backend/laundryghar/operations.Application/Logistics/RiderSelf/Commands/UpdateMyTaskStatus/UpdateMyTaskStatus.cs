using System.Text.Json;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.Commerce;
using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Entities.Kernel;
using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using laundryghar.SharedDataModel.Enums;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.Common;
using operations.Application.Logistics.RiderSelf.Dtos;

namespace operations.Application.Logistics.RiderSelf.Commands.UpdateMyTaskStatus;

// ── Update my task status (start / arrive / collect / complete / fail) ──────────

/// <param name="FailureReason">Reason code persisted on the assignment when Status='failed' (nullable).</param>
/// <param name="FailureNote">Optional free-text note for a failed status (nullable).</param>
public sealed record UpdateMyTaskStatusCommand(
    Guid AssignmentId, Guid UserId, Guid BrandId, string Status,
    string? FailureReason = null, string? FailureNote = null) : ICommand<RiderTaskResult>;

public sealed class UpdateMyTaskStatusHandler : ICommandHandler<UpdateMyTaskStatusCommand, RiderTaskResult>
{
    private readonly IOperationsDbContext _db;
    public UpdateMyTaskStatusHandler(IOperationsDbContext db) => _db = db;

    private static readonly string[] Allowed =
        ["started", "arrived", "collected", "completed", "failed"];

    public async Task<RiderTaskResult> HandleAsync(UpdateMyTaskStatusCommand command, CancellationToken cancellationToken)
    {
        var ct  = cancellationToken;
        var cmd = command;
        if (!Allowed.Contains(cmd.Status))
            return RiderTaskResult.Conflict($"Unsupported status '{cmd.Status}'.");

        var rider = await _db.Riders
            .Where(r => r.UserId == cmd.UserId && r.BrandId == cmd.BrandId)
            .Select(r => new { r.Id }).FirstOrDefaultAsync(ct);
        if (rider is null) return RiderTaskResult.NotFound();

        var da = await _db.DeliveryAssignments
            .FirstOrDefaultAsync(x => x.Id == cmd.AssignmentId
                                   && x.RiderId == rider.Id
                                   && x.BrandId == cmd.BrandId, ct);
        if (da is null) return RiderTaskResult.NotFound();

        var (o, c, addr) = await LoadOrderAsync(da, ct);
        var now = DateTimeOffset.UtcNow;
        var payoutCfg = await PayoutConfig.LoadAsync(_db, cmd.BrandId, ct);

        // "collected" is a pickup sub-step, not a DB status. Record collected_at and keep
        // the leg 'arrived' (collection happens on-site); the rider then drives to the store.
        if (cmd.Status == "collected")
        {
            if (da.LegType != "pickup")
                return RiderTaskResult.Conflict("Only pickup legs can be collected.");
            da.CollectedAt ??= now;
            if (da.Status == "started") { da.Status = "arrived"; da.ArrivedAt ??= now; }

            // COD-CASH (pickup leg): collecting the items IS collecting the cash. Stamp
            // cod_collected_at and ensure cod_amount is set so this collection enters the
            // SAME rider-cash settlement pipeline the delivery legs use. Only when cash is
            // actually due (PaymentPreference == "cod" && amount > 0). Idempotent via ??=.
            var cod = da.CodAmount ?? await ResolvePickupCodAsync(da, ct);
            if (cod is > 0m)
            {
                da.CodAmount ??= cod;
                da.CodCollectedAt ??= now;
            }

            da.UpdatedAt = now;
            da.UpdatedBy = cmd.UserId;

            // DEFECT 6: collection reflects "picked_up" in the order lifecycle. Advance
            // the linked order (if any) up to picked_up through the legal path.
            await AdvancePickupLegAsync(da, pickupTarget: null, orderTarget: OrderStatus.PickedUp,
                                        now: now, userId: cmd.UserId, ct: ct);
            return RiderTaskResult.Ok(RiderTaskMapper.ToDto(da, o, c, addr, payoutCfg));
        }

        // Any leg with an OTP (pickup or delivery) must be verified before completing.
        var legOtp = da.LegType == "pickup" ? o?.PickupOtp : o?.DeliveryOtp;
        if (cmd.Status == "completed"
            && !string.IsNullOrWhiteSpace(legOtp)
            && !da.OtpVerified)
            return RiderTaskResult.Conflict("OTP must be verified before completing.");

        da.Status = cmd.Status;
        switch (cmd.Status)
        {
            case "started": da.StartedAt ??= now; break;
            case "arrived": da.ArrivedAt ??= now; break;
            case "failed":
                // Persist the structured reason code and free-text note provided by the rider.
                if (!string.IsNullOrWhiteSpace(cmd.FailureReason))
                    da.CancellationReason ??= cmd.FailureReason.Trim();
                if (!string.IsNullOrWhiteSpace(cmd.FailureNote))
                    da.Notes ??= cmd.FailureNote.Trim();
                break;
            case "completed":
                da.CompletedAt ??= now;
                // Completing a pickup IS the drop-at-laundry confirmation — stamp the
                // drop if the store geofence hadn't already caught it.
                if (da.LegType == "pickup")
                {
                    da.DroppedAt ??= now;
                }
                else
                {
                    // Delivery/return — if the order still has a balance due, the rider
                    // collected it as COD cash. Record it for settlement (Phase 3).
                    var due = o?.AmountDue ?? 0m;
                    if (da.CodAmount is null && o?.PaymentStatus != "paid" && due > 0m)
                    {
                        da.CodAmount = due;
                        da.CodCollectedAt = now;
                    }
                }
                // Phase 4: persist the leg payout from the configured rates.
                da.PayoutAmount ??= payoutCfg.Compute(da.DistanceKm, o?.IsExpress ?? false, da.CodAmount is > 0m);
                break;
        }
        da.UpdatedAt = now;
        da.UpdatedBy = cmd.UserId;

        // ── DEFECT 6: pickup-leg completion advances the pickup request + order ────
        if (cmd.Status == "completed" && da.LegType == "pickup")
        {
            await AdvancePickupLegAsync(da, pickupTarget: "completed", orderTarget: OrderStatus.Received,
                                        now: now, userId: cmd.UserId, ct: ct);
        }

        // ── DOC-1: Transactional delivery-completion side-effects ─────────────────
        // When a delivery/return leg completes, atomically transition the order to
        // delivered, append a status-history row, create a COD payment row (if cash
        // was collected), and emit a delivery.completed outbox event. All mutations
        // share one SaveChanges inside a retry-capable transaction.
        var isDeliveryCompletion = cmd.Status == "completed"
                                && da.LegType is "delivery" or "return"
                                && o is not null;

        await _db.ExecuteInTransactionAsync(async innerCt =>
        {
            // H1 idempotency: the ENTIRE side-effect block is gated on first-completion
            // (DeliveredAt == null). A double-tap on an already-delivered order is a no-op.
            if (isDeliveryCompletion && o!.DeliveredAt == null)
            {
                // 1. Order status transition
                o.Status = "delivered";
                o.DeliveredAt = now;
                o.Version += 1;
                o.UpdatedAt = now;
                o.UpdatedBy = cmd.UserId;

                // 2. Order status history
                _db.OrderStatusHistories.Add(new OrderStatusHistory
                {
                    Id = Guid.NewGuid(),
                    OrderId = o.Id,
                    OrderCreatedAt = o.CreatedAt,
                    BrandId = da.BrandId,
                    FromStatus = "out_for_delivery",
                    ToStatus = "delivered",
                    ChangedAt = now,
                    ChangedByType = "system",
                    ChangedById = cmd.UserId,
                    Reason = "Delivery completed by rider",
                    CustomerNotified = false,
                    Metadata = "{}",
                    CreatedAt = now,
                    CreatedBy = cmd.UserId,
                });

                // 3. COD payment row — only when cash was collected AND no completed
                //    payment already exists for this order (re-call guard).
                if (da.CodAmount is > 0m)
                {
                    var codAlreadyRecorded = await _db.Payments
                        .AnyAsync(p => p.Gateway == "cod"
                                    && p.OrderId == o.Id
                                    && p.BrandId == da.BrandId
                                    && p.Status == CommercePaymentStatus.Succeeded, innerCt);

                    if (!codAlreadyRecorded)
                    {
                        var cod = da.CodAmount!.Value;
                        _db.Payments.Add(new Payment
                        {
                            Id = Guid.NewGuid(),
                            BrandId = da.BrandId,
                            CustomerId = o.CustomerId,
                            OrderId = o.Id,
                            OrderCreatedAt = o.CreatedAt,
                            // Must use constraint-valid values: "order" purpose, "succeeded" status.
                            PaymentPurpose = PaymentPurpose.Order,
                            PaymentNumber = $"COD-{o.OrderNumber}-{now:yyyyMMddHHmmss}",
                            Amount = cod,
                            ConvenienceFee = 0m,
                            GatewayCharge = 0m,
                            NetAmount = cod,
                            CurrencyCode = o.CurrencyCode,
                            Direction = 1,
                            Gateway = "cod",
                            Status = CommercePaymentStatus.Succeeded,
                            InitiatedAt = now,
                            CompletedAt = now,
                            Metadata = "{}",
                            CreatedAt = now,
                            UpdatedAt = now,
                            CreatedBy = cmd.UserId,
                            UpdatedBy = cmd.UserId,
                        });

                        o.AmountPaid += cod;
                        if (o.AmountPaid >= o.GrandTotal)
                            o.PaymentStatus = "paid";
                    }
                }

                // 4. Outbox event
                _db.OutboxEvents.Add(new OutboxEvent
                {
                    Id = Guid.NewGuid(),
                    BrandId = da.BrandId,
                    AggregateType = "order",
                    AggregateId = o.Id,
                    EventType = "delivery.completed",
                    EventVersion = 1,
                    Payload = JsonSerializer.Serialize(new
                    {
                        orderId = o.Id,
                        orderNumber = o.OrderNumber,
                        brandId = da.BrandId,
                        riderId = da.RiderId,
                        assignmentId = da.Id,
                        legType = da.LegType,
                        codCollected = da.CodAmount ?? 0m,
                        deliveredAt = now,
                    }),
                    Metadata = "{}",
                    OccurredAt = now,
                    Status = "pending",
                    CreatedAt = now,
                    CreatedBy = cmd.UserId,
                });
            }

            await _db.SaveChangesAsync(innerCt);
        }, ct);

        // Decrement current_load when this leg reaches a terminal state.
        if (cmd.Status is "completed" or "failed")
            await RiderLoad.DecrementAsync(_db, da.RiderId, ct);

        // FOLLOW-UP: incentive evaluation on a completed delivery is intentionally
        // dropped here. The legacy IncentiveEvaluator depends on the Logistics
        // Incentives sub-area + the Orders Fare config, neither of which is in the
        // migration scope of this slice. It was best-effort (wrapped in try/catch and
        // never blocked completion), so omitting it changes no completion behaviour —
        // only that incentive awards are not auto-created until Incentives is migrated.

        return RiderTaskResult.Ok(RiderTaskMapper.ToDto(da, o, c, addr, payoutCfg));
    }

    /// <summary>
    /// COD-CASH (pickup leg): resolves the cash due for a pickup leg from its linked
    /// pickup request, applying the SAME rule as assign-time
    /// (<see cref="PickupCod.ResolvePickupCodAmount"/>): cash is due only when
    /// PaymentPreference == "cod" and EstimatedAmount &gt; 0. Returns null for non-pickup
    /// legs, unlinked legs, or non-COD preferences.
    /// </summary>
    private async Task<decimal?> ResolvePickupCodAsync(DeliveryAssignment da, CancellationToken ct)
    {
        if (da.LegType != "pickup" || da.PickupRequestId is null) return null;
        var pr = await _db.PickupRequests
            .Where(p => p.Id == da.PickupRequestId.Value)
            .Select(p => new { p.PaymentPreference, p.EstimatedAmount })
            .FirstOrDefaultAsync(ct);
        if (pr is null) return null;
        return PickupCod.ResolvePickupCodAmount(pr.PaymentPreference, pr.EstimatedAmount);
    }

    private async Task<(Order?, Customer?, CustomerAddress?)> LoadOrderAsync(DeliveryAssignment da, CancellationToken ct)
    {
        if (da.OrderId is null || da.OrderCreatedAt is null) return (null, null, null);

        var o = await _db.Orders.FirstOrDefaultAsync(
            x => x.Id == da.OrderId && x.CreatedAt == da.OrderCreatedAt, ct);
        if (o is null) return (null, null, null);

        var c = await _db.Customers.FirstOrDefaultAsync(x => x.Id == o.CustomerId, ct);
        var addrId = da.LegType == "pickup" ? o.PickupAddressId : o.DeliveryAddressId;
        var addr = addrId.HasValue
            ? await _db.CustomerAddresses.FirstOrDefaultAsync(a => a.Id == addrId.Value, ct)
            : null;
        return (o, c, addr);
    }

    /// <summary>
    /// DEFECT 6 — advances a pickup leg's pickup_request and (when linked) its order to
    /// reflect physical progress. Resolution path:
    ///   delivery_assignment.pickup_request_id → pickup_request.converted_order_id → order
    /// (an order linked directly via assignment.order_id is also honoured). Each order hop
    /// along the legal happy-path writes an order_status_history row. Idempotent. Runs in a
    /// retry-capable transaction.
    /// </summary>
    private async Task AdvancePickupLegAsync(
        DeliveryAssignment da, string? pickupTarget, string orderTarget,
        DateTimeOffset now, Guid userId, CancellationToken ct)
    {
        // Resolve the pickup request (the canonical link for a pickup leg).
        PickupRequest? pr = da.PickupRequestId.HasValue
            ? await _db.PickupRequests.FirstOrDefaultAsync(p => p.Id == da.PickupRequestId.Value, ct)
            : null;

        // Resolve the linked order: prefer the assignment's own order link, else the
        // pickup request's converted order.
        Order? order = null;
        if (da.OrderId is not null && da.OrderCreatedAt is not null)
        {
            order = await _db.Orders.FirstOrDefaultAsync(
                x => x.Id == da.OrderId && x.CreatedAt == da.OrderCreatedAt, ct);
        }
        else if (pr?.ConvertedOrderId is not null)
        {
            order = await _db.Orders.FirstOrDefaultAsync(x => x.Id == pr.ConvertedOrderId.Value, ct);
        }

        // Nothing to advance and nothing to persist beyond the caller's own changes.
        if (pr is null && order is null) return;

        await _db.ExecuteInTransactionAsync(async innerCt =>
        {
            // 1. Advance the pickup request status (idempotent; only forward).
            if (pr is not null && pickupTarget is not null
                && !string.Equals(pr.Status, pickupTarget, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(pr.Status, "cancelled", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(pr.Status, "converted", StringComparison.OrdinalIgnoreCase))
            {
                pr.Status = pickupTarget;
                pr.UpdatedAt = now;
                pr.UpdatedBy = userId;
            }

            // 2. Walk the order forward along legal single-step transitions, writing a
            //    history row per hop. Off-path / already-advanced orders → no-op.
            if (order is not null)
            {
                var hops = OrderStateMachine.ForwardPath(order.Status, orderTarget, order.JobType);
                foreach (var next in hops)
                {
                    var from = order.Status;
                    order.Status = next;
                    order.Version += 1;
                    order.UpdatedAt = now;
                    order.UpdatedBy = userId;

                    if (next == OrderStatus.PickedUp) order.PickedUpAt ??= now;
                    if (next == OrderStatus.Received) order.ReceivedAt ??= now;

                    _db.OrderStatusHistories.Add(new OrderStatusHistory
                    {
                        Id = Guid.NewGuid(),
                        OrderId = order.Id,
                        OrderCreatedAt = order.CreatedAt,
                        BrandId = da.BrandId,
                        FromStatus = from,
                        ToStatus = next,
                        ChangedAt = now,
                        ChangedByType = "system",
                        ChangedById = userId,
                        Reason = "Pickup leg progressed by rider",
                        CustomerNotified = false,
                        Metadata = "{}",
                        CreatedAt = now,
                        CreatedBy = userId,
                    });
                }
            }

            await _db.SaveChangesAsync(innerCt);
        }, ct);
    }
}

using System.Text.Json;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.SharedDataModel.Entities.Kernel;
using laundryghar.SharedDataModel.Entities.Logistics;
using laundryghar.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.PartnerBookings.Common;
using operations.Application.Logistics.PartnerBookings.Dtos;

namespace operations.Application.Logistics.PartnerBookings.Commands.CreatePartnerBooking;

/// <param name="Request">The pickup/drop snapshot + quoted fare.</param>
/// <param name="ActorId">The partner user raising the booking (JWT sub) — becomes created_by_partner_user_id.</param>
public sealed record CreatePartnerBookingCommand(CreatePartnerBookingRequest Request, Guid? ActorId)
    : ICommand<PartnerBookingDto>;

/// <summary>
/// Creates a RaaS partner booking. partner_id is sourced from the tenant context (the partner_id
/// claim), NOT the request: the rls_partner WITH CHECK on partner_bookings rejects any insert whose
/// partner_id differs from app.current_partner_id, so a cross-partner booking is impossible.
///
/// PREPAID ECONOMICS (FULL-11): a booking with a positive quoted fare is pre-funded from the
/// partner's prepaid wallet:
///   (1) A balance SUFFICIENCY pre-check reads the caller's wallet available_balance (RLS auto-
///       scopes the wallet to this partner) and rejects the booking with a
///       <see cref="BusinessRuleException"/> when the balance cannot cover the quoted fare.
///   (2) On success a <c>partner_booking.debit_wallet</c> outbox event is written in the SAME
///       <see cref="IOperationsDbContext.SaveChangesAsync"/> as the booking insert, so the booking
///       and its debit obligation commit atomically. The commerce-host worker
///       (<c>PartnerBookingDebitService</c>) consumes it and dispatches
///       <c>DebitPartnerWalletCommand</c>; the debit is idempotent on booking id, so a redelivery
///       is a no-op. The pre-check is best-effort (it narrows the race window); the debit consumer
///       is the authoritative guard and cancels the booking if the balance vanished in the interim.
/// </summary>
public sealed class CreatePartnerBookingHandler : ICommandHandler<CreatePartnerBookingCommand, PartnerBookingDto>
{
    /// <summary>Outbox event type the commerce-host debit consumer subscribes to.</summary>
    public const string DebitWalletEventType = "partner_booking.debit_wallet";

    private readonly IOperationsDbContext _db;
    private readonly ICurrentTenant _tenant;

    public CreatePartnerBookingHandler(IOperationsDbContext db, ICurrentTenant tenant)
    {
        _db     = db;
        _tenant = tenant;
    }

    public async Task<PartnerBookingDto> HandleAsync(CreatePartnerBookingCommand cmd, CancellationToken ct)
    {
        var partnerId = _tenant.PartnerId
            ?? throw new UnauthorizedAccessException("Partner context required.");
        var partnerUserId = cmd.ActorId
            ?? throw new UnauthorizedAccessException("Partner user context required.");

        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;
        var bookingId = Guid.NewGuid();

        // A booking only draws on the wallet when a positive fare was quoted. A null/zero fare
        // carries no charge, so neither the pre-check nor the debit event applies.
        var chargeable = req.QuotedFare is > 0m;
        var fare = req.QuotedFare ?? 0m;

        if (chargeable)
        {
            // Prepaid sufficiency pre-check. rls_partner already scopes the wallet to the caller;
            // the explicit partner_id filter documents intent. A missing wallet (create-on-first-
            // read has not fired yet) or a non-active wallet means zero available balance.
            var wallet = await _db.PartnerWalletAccounts.AsNoTracking()
                .FirstOrDefaultAsync(w => w.PartnerId == partnerId, ct);

            var available = wallet is { Status: "active" } ? wallet.AvailableBalance ?? 0m : 0m;

            if (available < fare)
                throw new BusinessRuleException(
                    $"Insufficient partner wallet balance for this booking (available {available:0.00}, " +
                    $"required {fare:0.00}). Please top up the wallet before booking.");
        }

        var e = new PartnerBooking
        {
            Id                     = bookingId,
            PartnerId              = partnerId,
            BrandId                = req.BrandId,
            CreatedByPartnerUserId = partnerUserId,
            PickupSnapshot         = PartnerBookingMapper.Serialize(req.Pickup),
            DropSnapshot           = PartnerBookingMapper.Serialize(req.Drop),
            QuotedFare             = req.QuotedFare,
            Status                 = "requested",
            CreatedAt              = now,
            UpdatedAt              = now,
            CreatedBy              = partnerUserId,
            UpdatedBy              = partnerUserId,
        };

        _db.PartnerBookings.Add(e);

        if (chargeable)
        {
            // Transactional-outbox emit: same change-tracker, same SaveChanges as the booking →
            // booking + debit obligation are committed atomically or not at all. kernel.outbox_events
            // is a cross-brand/cross-BC table (RLS inert), so this partner-session insert is allowed
            // and the row is visible to the commerce host (same physical DB).
            _db.OutboxEvents.Add(new OutboxEvent
            {
                Id            = Guid.NewGuid(),
                BrandId       = req.BrandId,
                AggregateType = "partner_booking",
                AggregateId   = bookingId,
                EventType     = DebitWalletEventType,
                EventVersion  = 1,
                Payload       = JsonSerializer.Serialize(new
                {
                    partnerId,
                    bookingId,
                    amount = fare
                }),
                Metadata      = "{}",
                OccurredAt    = now,
                Status        = "pending",
                CreatedAt     = now,
                CreatedBy     = partnerUserId,
            });
        }

        await _db.SaveChangesAsync(ct);

        return PartnerBookingMapper.ToDto(e);
    }
}

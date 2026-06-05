using FluentValidation;
using laundryghar.Commerce.Application;
using laundryghar.Commerce.Infrastructure.Gateway;
using MediatR;

namespace laundryghar.Commerce.Application.Customer.Payments;

// ── Initiate Payment (idempotent) ─────────────────────────────────────────────

/// <summary>
/// Creates a payment record and a gateway order.
/// Idempotency: if the Idempotency-Key header value matches an existing payment for this
/// customer+brand, the original payment is returned without creating a duplicate.
/// </summary>
public sealed record InitiatePaymentCommand(
    Guid CustomerId,
    Guid BrandId,
    InitiatePaymentRequest Request,
    string IdempotencyKey
) : IRequest<PaymentDto>;

public sealed class InitiatePaymentHandler : IRequestHandler<InitiatePaymentCommand, PaymentDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly IPaymentGateway _gateway;

    public InitiatePaymentHandler(LaundryGharDbContext db, IPaymentGateway gateway)
    {
        _db = db;
        _gateway = gateway;
    }

    public async Task<PaymentDto> Handle(InitiatePaymentCommand cmd, CancellationToken ct)
    {
        // Idempotency: return existing payment if key already used
        var existing = await _db.Payments
            .FirstOrDefaultAsync(p => p.IdempotencyKey == cmd.IdempotencyKey
                                   && p.BrandId == cmd.BrandId
                                   && p.CustomerId == cmd.CustomerId, ct);
        if (existing is not null)
            return ToDto(existing);

        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        // Create gateway order
        var receipt = $"pay_{cmd.CustomerId:N}_{now:yyyyMMddHHmmss}";
        var gatewayResult = await _gateway.CreateOrderAsync(req.Amount, req.CurrencyCode, receipt, ct);

        var paymentNumber = $"PAY-{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..28];

        var payment = new Payment
        {
            Id              = Guid.NewGuid(),
            BrandId         = cmd.BrandId,
            CustomerId      = cmd.CustomerId,
            PaymentMethodId = req.PaymentMethodId,
            PaymentPurpose  = req.PaymentPurpose,
            PaymentNumber   = paymentNumber,
            Amount          = req.Amount,
            ConvenienceFee  = 0m,
            GatewayCharge   = 0m,
            NetAmount       = req.Amount,
            CurrencyCode    = req.CurrencyCode,
            Direction       = 1,  // inbound
            Gateway         = gatewayResult.Gateway,
            GatewayOrderId  = gatewayResult.GatewayOrderId,
            GatewayResponse = gatewayResult.RawResponse,
            OrderId         = req.OrderId,
            OrderCreatedAt  = req.OrderCreatedAt,
            Notes           = req.Notes,
            Status          = "pending",
            InitiatedAt     = now,
            IdempotencyKey  = cmd.IdempotencyKey,
            Metadata        = "{}",
            CreatedAt       = now,
            UpdatedAt       = now,
            CreatedBy       = cmd.CustomerId
        };

        _db.Payments.Add(payment);
        await _db.SaveChangesAsync(ct);
        return ToDto(payment);
    }

    internal static PaymentDto ToDto(Payment x) => new(
        x.Id, x.BrandId, x.CustomerId, x.PaymentPurpose, x.PaymentNumber,
        x.Amount, x.ConvenienceFee, x.GatewayCharge, x.NetAmount, x.CurrencyCode,
        x.Direction, x.Gateway, x.GatewayOrderId, x.GatewayPaymentId,
        x.Status, x.FailureCode, x.FailureMessage, x.InitiatedAt, x.CompletedAt,
        x.FailedAt, x.IdempotencyKey, x.CreatedAt, x.UpdatedAt);
}

public sealed class InitiatePaymentValidator : AbstractValidator<InitiatePaymentCommand>
{
    public InitiatePaymentValidator()
    {
        RuleFor(x => x.Request.Amount).GreaterThan(0);
        RuleFor(x => x.Request.CurrencyCode).NotEmpty().Length(3);
        RuleFor(x => x.Request.PaymentPurpose).NotEmpty();
        RuleFor(x => x.IdempotencyKey).NotEmpty();
    }
}

// ── Verify Payment ─────────────────────────────────────────────────────────────

/// <summary>
/// Verifies the gateway signature and marks the payment captured.
/// The caller must specify the purpose so the handler can trigger the correct
/// post-payment action (package activation or wallet credit).
/// </summary>
public sealed record VerifyPaymentCommand(
    Guid CustomerId,
    Guid BrandId,
    VerifyPaymentRequest Request
) : IRequest<PaymentDto>;

public sealed class VerifyPaymentHandler : IRequestHandler<VerifyPaymentCommand, PaymentDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly IPaymentGateway _gateway;

    public VerifyPaymentHandler(LaundryGharDbContext db, IPaymentGateway gateway)
    {
        _db = db;
        _gateway = gateway;
    }

    public async Task<PaymentDto> Handle(VerifyPaymentCommand cmd, CancellationToken ct)
    {
        var req = cmd.Request;

        var payment = await _db.Payments
            .FirstOrDefaultAsync(p => p.Id == req.PaymentId
                                   && p.BrandId == cmd.BrandId
                                   && p.CustomerId == cmd.CustomerId, ct);

        if (payment is null)
            throw new BusinessRuleException("Payment not found.");
        if (payment.Status == "captured" || payment.Status == "completed")
            return InitiatePaymentHandler.ToDto(payment); // idempotent re-verify
        if (payment.Status != "pending")
            throw new BusinessRuleException($"Cannot verify payment with status '{payment.Status}'.");

        var isValid = await _gateway.VerifySignatureAsync(
            req.GatewayOrderId, req.GatewayPaymentId, req.GatewaySignature, ct);

        var now = DateTimeOffset.UtcNow;

        if (!isValid)
        {
            payment.Status          = "failed";
            payment.FailedAt        = now;
            payment.FailureCode     = "SIGNATURE_MISMATCH";
            payment.FailureMessage  = "Gateway signature verification failed.";
            payment.UpdatedAt       = now;
            await _db.SaveChangesAsync(ct);
            throw new BusinessRuleException("Payment signature verification failed.");
        }

        payment.Status           = "captured";
        payment.GatewayPaymentId = req.GatewayPaymentId;
        payment.GatewaySignature = req.GatewaySignature;
        payment.CompletedAt      = now;
        payment.UpdatedAt        = now;
        await _db.SaveChangesAsync(ct);

        return InitiatePaymentHandler.ToDto(payment);
    }
}

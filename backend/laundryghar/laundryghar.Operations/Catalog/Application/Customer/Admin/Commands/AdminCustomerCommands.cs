using laundryghar.Catalog.Infrastructure.Auth;
using laundryghar.Catalog.Infrastructure.Services;
using FluentValidation;
using laundryghar.Catalog.Application.Customer.Admin.Dtos;
using laundryghar.Catalog.Application.Customer.Admin.Queries;
using MediatR;

namespace laundryghar.Catalog.Application.Customer.Admin.Commands;

// ── Create customer ───────────────────────────────────────────────────────────

/// <summary>
/// Creates a customer from the counter/admin side (no OTP flow).
/// Phone must be E.164 and unique per brand. Reuses the same customer_code
/// generation logic as OTP signup (CustomerOtpVerifyHandler).
/// Returns 422 (BusinessRuleException) on duplicate phone within the brand.
/// </summary>
public sealed record AdminCreateCustomerCommand(
    AdminCreateCustomerRequest Request,
    Guid? ActorId
) : IRequest<AdminCustomerDto>;

public sealed class AdminCreateCustomerHandler : IRequestHandler<AdminCreateCustomerCommand, AdminCustomerDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public AdminCreateCustomerHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<AdminCustomerDto> Handle(AdminCreateCustomerCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req     = cmd.Request;
        var now     = DateTimeOffset.UtcNow;

        // Unique phone per brand (friendly 422 rather than PG unique violation).
        var duplicate = await _db.Customers
            .AnyAsync(c => c.BrandId == brandId && c.PhoneE164 == req.Phone && c.DeletedAt == null, ct);
        if (duplicate)
            throw new BusinessRuleException($"A customer with phone {req.Phone} already exists in this brand.");

        var customerCode = await GenerateUniqueCodeAsync(brandId, ct);

        var customer = new SharedDataModel.Entities.CustomerCatalog.Customer
        {
            Id               = Guid.NewGuid(),
            BrandId          = brandId,
            CustomerCode     = customerCode,
            PhoneE164        = req.Phone,
            FirstName        = req.FirstName?.Trim(),
            LastName         = req.LastName?.Trim(),
            Email            = req.Email?.Trim().ToLowerInvariant(),
            // Admin-created customers do not yet have verified phone — they can verify via OTP later.
            PhoneVerifiedAt  = null,
            Locale           = "en-IN",
            Timezone         = "Asia/Kolkata",
            Status           = "active",
            Metadata         = "{}",
            Tags             = [],
            LifetimeOrders   = 0,
            LifetimeSpend    = 0,
            LoyaltyPointsBalance = 0,
            WalletBalance    = 0,
            // DPDP Act 2023: all marketing-class opt-ins default to false.
            MarketingOptIn   = false,
            SmsOptIn         = false,
            WhatsappOptIn    = false,
            EmailOptIn       = false,
            PushOptIn        = false,
            CreatedAt        = now,
            UpdatedAt        = now,
            CreatedBy        = cmd.ActorId,
            UpdatedBy        = cmd.ActorId,
            Version          = 1
        };

        _db.Customers.Add(customer);
        await _db.SaveChangesAsync(ct);
        return GetCustomersHandler.ToDto(customer);
    }

    private async Task<string> GenerateUniqueCodeAsync(Guid brandId, CancellationToken ct)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        for (int attempt = 0; attempt < 10; attempt++)
        {
            var code = new string(Enumerable.Range(0, 10)
                .Select(_ => System.Security.Cryptography.RandomNumberGenerator.GetInt32(chars.Length))
                .Select(i => chars[i])
                .ToArray());
            if (!await _db.Customers.AnyAsync(c => c.BrandId == brandId && c.CustomerCode == code, ct))
                return code;
        }
        return Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
    }
}

public sealed class AdminCreateCustomerValidator : AbstractValidator<AdminCreateCustomerCommand>
{
    // E.164: starts with +, 7–15 digits.
    private static readonly System.Text.RegularExpressions.Regex E164Regex =
        new(@"^\+[1-9]\d{6,14}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public AdminCreateCustomerValidator()
    {
        RuleFor(x => x.Request.Phone)
            .NotEmpty().WithMessage("Phone is required.")
            .Matches(E164Regex).WithMessage("Phone must be in E.164 format (e.g. +919810001001).");

        RuleFor(x => x.Request.Email)
            .EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Request.Email))
            .WithMessage("Email must be a valid email address.");

        RuleFor(x => x.Request.FirstName)
            .MaximumLength(100).When(x => x.Request.FirstName is not null);

        RuleFor(x => x.Request.LastName)
            .MaximumLength(100).When(x => x.Request.LastName is not null);
    }
}

// ─────────────────────────────────────────────────────────────────────────────

public sealed record AdminUpdateCustomerCommand(
    Guid Id,
    AdminUpdateCustomerRequest Request,
    Guid? ActorId
) : IRequest<AdminCustomerDto?>;

public sealed class AdminUpdateCustomerHandler : IRequestHandler<AdminUpdateCustomerCommand, AdminCustomerDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public AdminUpdateCustomerHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<AdminCustomerDto?> Handle(AdminUpdateCustomerCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.Customers
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (e is null || e.DeletedAt != null) return null;

        var req = cmd.Request;
        e.FirstName       = req.FirstName ?? e.FirstName;
        e.LastName        = req.LastName  ?? e.LastName;
        e.Email           = req.Email     ?? e.Email;
        e.Gender          = req.Gender    ?? e.Gender;
        e.DateOfBirth     = req.DateOfBirth ?? e.DateOfBirth;
        e.CustomerSegment = req.CustomerSegment ?? e.CustomerSegment;
        e.RiskFlag        = req.RiskFlag ?? e.RiskFlag;
        e.UpdatedAt       = DateTimeOffset.UtcNow;
        e.UpdatedBy       = cmd.ActorId;
        e.Version++;

        await _db.SaveChangesAsync(ct);
        return GetCustomersHandler.ToDto(e);
    }
}

public sealed record AdminDeleteCustomerCommand(Guid Id, Guid? ActorId) : IRequest<bool>;

public sealed class AdminDeleteCustomerHandler : IRequestHandler<AdminDeleteCustomerCommand, bool>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public AdminDeleteCustomerHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<bool> Handle(AdminDeleteCustomerCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.Customers
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (e is null || e.DeletedAt != null) return false;

        e.DeletedAt = DateTimeOffset.UtcNow;
        e.UpdatedAt = DateTimeOffset.UtcNow;
        e.UpdatedBy = cmd.ActorId;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

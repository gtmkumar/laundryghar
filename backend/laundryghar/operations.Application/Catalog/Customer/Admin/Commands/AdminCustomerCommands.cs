using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Catalog.Customer.Admin.Dtos;
using operations.Application.Catalog.Customer.Admin.Queries;
using operations.Application.Common.Interfaces;

namespace operations.Application.Catalog.Customer.Admin.Commands;

// ── Create customer ───────────────────────────────────────────────────────────

/// <summary>
/// Creates a customer from the counter/admin side (no OTP flow).
/// Phone must be E.164 and unique per brand. Reuses the same customer_code
/// generation logic as OTP signup.
/// Returns 422 (BusinessRuleException) on duplicate phone within the brand.
/// </summary>
public sealed record AdminCreateCustomerCommand(
    AdminCreateCustomerRequest Request,
    Guid? ActorId
) : ICommand<AdminCustomerDto>;

public sealed class AdminCreateCustomerHandler : ICommandHandler<AdminCreateCustomerCommand, AdminCustomerDto>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public AdminCreateCustomerHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<AdminCustomerDto> HandleAsync(AdminCreateCustomerCommand cmd, CancellationToken ct)
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

        var customer = new laundryghar.SharedDataModel.Entities.CustomerCatalog.Customer
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

public sealed class AdminCreateCustomerValidator : AbstractValidator<AdminCreateCustomerRequest>
{
    // E.164: starts with +, 7–15 digits.
    private static readonly System.Text.RegularExpressions.Regex E164Regex =
        new(@"^\+[1-9]\d{6,14}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public AdminCreateCustomerValidator()
    {
        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Phone is required.")
            .Matches(E164Regex).WithMessage("Phone must be in E.164 format (e.g. +919810001001).");

        RuleFor(x => x.Email)
            .EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email))
            .WithMessage("Email must be a valid email address.");

        RuleFor(x => x.FirstName)
            .MaximumLength(100).When(x => x.FirstName is not null);

        RuleFor(x => x.LastName)
            .MaximumLength(100).When(x => x.LastName is not null);
    }
}

// ─────────────────────────────────────────────────────────────────────────────

public sealed record AdminUpdateCustomerCommand(
    Guid Id,
    AdminUpdateCustomerRequest Request,
    Guid? ActorId
) : ICommand<AdminCustomerDto?>;

public sealed class AdminUpdateCustomerHandler : ICommandHandler<AdminUpdateCustomerCommand, AdminCustomerDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public AdminUpdateCustomerHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<AdminCustomerDto?> HandleAsync(AdminUpdateCustomerCommand cmd, CancellationToken ct)
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

public sealed record AdminDeleteCustomerCommand(Guid Id, Guid? ActorId) : ICommand<bool>;

public sealed class AdminDeleteCustomerHandler : ICommandHandler<AdminDeleteCustomerCommand, bool>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public AdminDeleteCustomerHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<bool> HandleAsync(AdminDeleteCustomerCommand cmd, CancellationToken ct)
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

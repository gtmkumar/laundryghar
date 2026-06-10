using FluentValidation;
using laundryghar.Catalog.Application.Customer.Self.Dtos;
using laundryghar.Catalog.Application.Customer.Self.Queries;
using MediatR;

namespace laundryghar.Catalog.Application.Customer.Self.Commands;

// ── Patch Profile ─────────────────────────────────────────────────────────────

public sealed record PatchMyProfileCommand(Guid CustomerId, PatchProfileRequest Request) : IRequest<CustomerProfileDto?>;

public sealed class PatchMyProfileHandler : IRequestHandler<PatchMyProfileCommand, CustomerProfileDto?>
{
    private readonly LaundryGharDbContext _db;

    public PatchMyProfileHandler(LaundryGharDbContext db) => _db = db;

    public async Task<CustomerProfileDto?> Handle(PatchMyProfileCommand cmd, CancellationToken ct)
    {
        // Self-filter: only allow patching own profile (CustomerId = sub)
        var c = await _db.Customers.FindAsync([cmd.CustomerId], ct);
        if (c is null || c.DeletedAt != null) return null;

        var req = cmd.Request;
        if (req.FirstName   is not null) c.FirstName   = req.FirstName;
        if (req.LastName    is not null) c.LastName    = req.LastName;
        if (req.Email       is not null) c.Email       = req.Email;
        if (req.Gender      is not null) c.Gender      = req.Gender;
        if (req.DateOfBirth is not null) c.DateOfBirth = req.DateOfBirth;
        if (req.AvatarUrl   is not null) c.AvatarUrl   = req.AvatarUrl;
        if (req.Locale      is not null) c.Locale      = req.Locale;
        if (req.Timezone    is not null) c.Timezone    = req.Timezone;
        if (req.MarketingOptIn.HasValue) c.MarketingOptIn = req.MarketingOptIn.Value;
        if (req.SmsOptIn.HasValue)       c.SmsOptIn       = req.SmsOptIn.Value;
        if (req.WhatsappOptIn.HasValue)  c.WhatsappOptIn  = req.WhatsappOptIn.Value;
        if (req.EmailOptIn.HasValue)     c.EmailOptIn     = req.EmailOptIn.Value;
        if (req.PushOptIn.HasValue)      c.PushOptIn      = req.PushOptIn.Value;

        c.UpdatedAt = DateTimeOffset.UtcNow;
        c.UpdatedBy = cmd.CustomerId;
        c.Version++;

        await _db.SaveChangesAsync(ct);
        return GetMyProfileHandler.ToDto(c);
    }
}

// ── Address CRUD ──────────────────────────────────────────────────────────────

public sealed record CreateMyAddressCommand(
    Guid CustomerId, Guid BrandId, CreateAddressRequest Request) : IRequest<CustomerAddressDto>;

public sealed class CreateMyAddressHandler : IRequestHandler<CreateMyAddressCommand, CustomerAddressDto>
{
    private readonly LaundryGharDbContext _db;

    public CreateMyAddressHandler(LaundryGharDbContext db) => _db = db;

    public async Task<CustomerAddressDto> Handle(CreateMyAddressCommand cmd, CancellationToken ct)
    {
        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        // Handle default-address flag: unset previous default if setting new one
        if (req.IsDefault)
        {
            await _db.CustomerAddresses
                .Where(a => a.CustomerId == cmd.CustomerId && a.IsDefault)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsDefault, false), ct);
        }

        var e = new CustomerAddress
        {
            Id                   = Guid.NewGuid(),
            CustomerId           = cmd.CustomerId,
            BrandId              = cmd.BrandId,
            Label                = req.Label,
            CustomLabel          = req.CustomLabel,
            RecipientName        = req.RecipientName,
            RecipientPhone       = req.RecipientPhone,
            AddressLine1         = req.AddressLine1,
            AddressLine2         = req.AddressLine2,
            Landmark             = req.Landmark,
            Floor                = req.Floor,
            FlatNumber           = req.FlatNumber,
            BuildingName         = req.BuildingName,
            Society              = req.Society,
            Area                 = req.Area,
            City                 = req.City,
            State                = req.State,
            Pincode              = req.Pincode,
            CountryCode          = req.CountryCode,
            DeliveryInstructions = req.DeliveryInstructions,
            IsDefault            = req.IsDefault,
            IsVerified           = false,
            UseCount             = 0,
            Status               = "active",
            CreatedAt            = now,
            UpdatedAt            = now,
            CreatedBy            = cmd.CustomerId,
            UpdatedBy            = cmd.CustomerId
        };

        _db.CustomerAddresses.Add(e);
        await _db.SaveChangesAsync(ct);
        return GetMyAddressesHandler.ToDto(e);
    }
}

public sealed record UpdateMyAddressCommand(
    Guid CustomerId, Guid AddressId, UpdateAddressRequest Request) : IRequest<CustomerAddressDto?>;

public sealed class UpdateMyAddressHandler : IRequestHandler<UpdateMyAddressCommand, CustomerAddressDto?>
{
    private readonly LaundryGharDbContext _db;

    public UpdateMyAddressHandler(LaundryGharDbContext db) => _db = db;

    public async Task<CustomerAddressDto?> Handle(UpdateMyAddressCommand cmd, CancellationToken ct)
    {
        // Self-filter: address must belong to this customer
        var e = await _db.CustomerAddresses
            .FirstOrDefaultAsync(a => a.Id == cmd.AddressId && a.CustomerId == cmd.CustomerId, ct);
        if (e is null || e.DeletedAt != null) return null;

        var req = cmd.Request;

        // Handle default-address flag
        if (req.IsDefault && !e.IsDefault)
        {
            await _db.CustomerAddresses
                .Where(a => a.CustomerId == cmd.CustomerId && a.IsDefault && a.Id != cmd.AddressId)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsDefault, false), ct);
        }

        e.Label                = req.Label;
        e.CustomLabel          = req.CustomLabel;
        e.RecipientName        = req.RecipientName;
        e.RecipientPhone       = req.RecipientPhone;
        e.AddressLine1         = req.AddressLine1;
        e.AddressLine2         = req.AddressLine2;
        e.Landmark             = req.Landmark;
        e.Floor                = req.Floor;
        e.FlatNumber           = req.FlatNumber;
        e.BuildingName         = req.BuildingName;
        e.Society              = req.Society;
        e.Area                 = req.Area;
        e.City                 = req.City;
        e.State                = req.State;
        e.Pincode              = req.Pincode;
        e.CountryCode          = req.CountryCode;
        e.DeliveryInstructions = req.DeliveryInstructions;
        e.IsDefault            = req.IsDefault;
        e.UpdatedAt            = DateTimeOffset.UtcNow;
        e.UpdatedBy            = cmd.CustomerId;

        await _db.SaveChangesAsync(ct);
        return GetMyAddressesHandler.ToDto(e);
    }
}

public sealed record DeleteMyAddressCommand(Guid CustomerId, Guid AddressId) : IRequest<bool>;

public sealed class DeleteMyAddressHandler : IRequestHandler<DeleteMyAddressCommand, bool>
{
    private readonly LaundryGharDbContext _db;

    public DeleteMyAddressHandler(LaundryGharDbContext db) => _db = db;

    public async Task<bool> Handle(DeleteMyAddressCommand cmd, CancellationToken ct)
    {
        var e = await _db.CustomerAddresses
            .FirstOrDefaultAsync(a => a.Id == cmd.AddressId && a.CustomerId == cmd.CustomerId, ct);
        if (e is null || e.DeletedAt != null) return false;

        e.DeletedAt = DateTimeOffset.UtcNow;
        e.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

// ── Device Registration ───────────────────────────────────────────────────────

public sealed record RegisterDeviceCommand(
    Guid CustomerId, Guid BrandId, RegisterDeviceRequest Request) : IRequest<CustomerDeviceDto>;

public sealed class RegisterDeviceHandler : IRequestHandler<RegisterDeviceCommand, CustomerDeviceDto>
{
    private readonly LaundryGharDbContext _db;

    public RegisterDeviceHandler(LaundryGharDbContext db) => _db = db;

    public async Task<CustomerDeviceDto> Handle(RegisterDeviceCommand cmd, CancellationToken ct)
    {
        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        // Upsert on (customer_id, device_id) — update if exists, insert if new
        var existing = await _db.CustomerDevices
            .FirstOrDefaultAsync(d => d.CustomerId == cmd.CustomerId && d.DeviceId == req.DeviceId, ct);

        if (existing is not null)
        {
            existing.Platform    = req.Platform;
            existing.OsVersion   = req.OsVersion;
            existing.DeviceModel = req.DeviceModel;
            existing.DeviceName  = req.DeviceName;
            existing.AppVersion  = req.AppVersion;
            existing.AppBuild    = req.AppBuild;
            existing.FcmToken    = req.FcmToken;
            existing.ApnsToken   = req.ApnsToken;
            existing.PushEnabled = req.PushEnabled;
            existing.Language    = req.Language;
            existing.Timezone    = req.Timezone;
            existing.LastSeenAt  = now;
            existing.IsActive    = true;

            await _db.SaveChangesAsync(ct);
            return ToDto(existing);
        }

        var e = new CustomerDevice
        {
            Id          = Guid.NewGuid(),
            CustomerId  = cmd.CustomerId,
            BrandId     = cmd.BrandId,
            DeviceId    = req.DeviceId,
            Platform    = req.Platform,
            OsVersion   = req.OsVersion,
            DeviceModel = req.DeviceModel,
            DeviceName  = req.DeviceName,
            AppVersion  = req.AppVersion,
            AppBuild    = req.AppBuild,
            FcmToken    = req.FcmToken,
            ApnsToken   = req.ApnsToken,
            PushEnabled = req.PushEnabled,
            Language    = req.Language,
            Timezone    = req.Timezone,
            FirstSeenAt = now,
            LastSeenAt  = now,
            IsActive    = true,
            Metadata    = "{}",
            CreatedAt   = now,
            CreatedBy   = cmd.CustomerId
        };

        _db.CustomerDevices.Add(e);
        await _db.SaveChangesAsync(ct);
        return ToDto(e);
    }

    private static CustomerDeviceDto ToDto(CustomerDevice d) => new(
        d.Id, d.CustomerId, d.DeviceId, d.Platform, d.OsVersion, d.DeviceModel,
        d.AppVersion, d.FcmToken, d.PushEnabled, d.IsActive, d.LastSeenAt, d.FirstSeenAt);
}

// ── DPDP Consents ─────────────────────────────────────────────────────────────

public sealed record GrantConsentCommand(
    Guid CustomerId, Guid BrandId, GrantConsentRequest Request) : IRequest<DpdpConsentDto>;

public sealed class GrantConsentHandler : IRequestHandler<GrantConsentCommand, DpdpConsentDto>
{
    private readonly LaundryGharDbContext _db;

    public GrantConsentHandler(LaundryGharDbContext db) => _db = db;

    public async Task<DpdpConsentDto> Handle(GrantConsentCommand cmd, CancellationToken ct)
    {
        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        var e = new DpdpConsent
        {
            Id                    = Guid.NewGuid(),
            CustomerId            = cmd.CustomerId,
            BrandId               = cmd.BrandId,
            Purpose               = req.Purpose,
            PurposeDescription    = req.PurposeDescription,
            DataCategories        = req.DataCategories,
            ConsentStatus         = "granted",
            ConsentMethod         = req.ConsentMethod,
            PrivacyPolicyVersion  = req.PrivacyPolicyVersion,
            TermsVersion          = req.TermsVersion,
            ConsentTextSnapshot   = req.ConsentTextSnapshot,
            GrantedAt             = now,
            Metadata              = "{}",
            CreatedAt             = now,
            CreatedBy             = cmd.CustomerId
        };

        _db.DpdpConsents.Add(e);
        await _db.SaveChangesAsync(ct);
        return GetMyConsentsHandler.ToDto(e);
    }
}

public sealed record WithdrawConsentCommand(
    Guid CustomerId, Guid BrandId, WithdrawConsentRequest Request) : IRequest<DpdpConsentDto?>;

public sealed class WithdrawConsentHandler : IRequestHandler<WithdrawConsentCommand, DpdpConsentDto?>
{
    private readonly LaundryGharDbContext _db;

    public WithdrawConsentHandler(LaundryGharDbContext db) => _db = db;

    public async Task<DpdpConsentDto?> Handle(WithdrawConsentCommand cmd, CancellationToken ct)
    {
        // DPDP consents are immutable — create a new "withdrawn" record.
        // H2: require an active granted consent to exist before withdrawing.
        // Returning null maps to 404 at the endpoint — prevents spurious withdrawn rows.
        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        var grantedConsent = await _db.DpdpConsents
            .Where(c => c.CustomerId == cmd.CustomerId
                     && c.BrandId    == cmd.BrandId
                     && c.Purpose    == req.Purpose
                     && c.ConsentStatus == "granted")
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);

        // No active grant found — return null so endpoint sends 404.
        if (grantedConsent is null) return null;

        var e = new DpdpConsent
        {
            Id                   = Guid.NewGuid(),
            CustomerId           = cmd.CustomerId,
            BrandId              = cmd.BrandId,          // from authenticated token, never Guid.Empty
            Purpose              = req.Purpose,
            PurposeDescription   = grantedConsent.PurposeDescription,
            DataCategories       = grantedConsent.DataCategories,
            ConsentStatus        = "withdrawn",
            ConsentMethod        = "explicit_checkbox",
            PrivacyPolicyVersion = req.PrivacyPolicyVersion,
            WithdrawnAt          = now,
            Metadata             = "{}",
            CreatedAt            = now,
            CreatedBy            = cmd.CustomerId
        };

        _db.DpdpConsents.Add(e);
        await _db.SaveChangesAsync(ct);
        return GetMyConsentsHandler.ToDto(e);
    }
}

// ── Account Deletion Request ──────────────────────────────────────────────────

public sealed record CreateDeletionRequestCommand(
    Guid CustomerId, Guid BrandId, CreateDeletionRequestRequest Request) : IRequest<AccountDeletionRequestDto>;

public sealed class CreateDeletionRequestHandler : IRequestHandler<CreateDeletionRequestCommand, AccountDeletionRequestDto>
{
    private readonly LaundryGharDbContext _db;

    public CreateDeletionRequestHandler(LaundryGharDbContext db) => _db = db;

    public async Task<AccountDeletionRequestDto> Handle(CreateDeletionRequestCommand cmd, CancellationToken ct)
    {
        // Idempotency: return an existing pending request instead of creating a duplicate.
        var existing = await _db.AccountDeletionRequests
            .Where(r => r.CustomerId == cmd.CustomerId && r.Status == "pending")
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
            return new AccountDeletionRequestDto(
                existing.Id, existing.Status, existing.RequestSource, existing.Reason,
                existing.RequestedAt, existing.GracePeriodEndsAt, existing.CancelledAt);

        var now = DateTimeOffset.UtcNow;

        var e = new AccountDeletionRequest
        {
            Id                  = Guid.NewGuid(),
            CustomerId          = cmd.CustomerId,
            BrandId             = cmd.BrandId,
            RequestSource       = cmd.Request.RequestSource,
            Reason              = cmd.Request.Reason,
            ReasonText          = cmd.Request.ReasonText,
            RequestedAt         = now,
            GracePeriodEndsAt   = now.AddDays(30), // 30-day DPDP grace period
            PendingOrdersCount  = 0,
            PendingAmount       = 0,
            Status              = "pending",
            Metadata            = "{}",
            CreatedAt           = now,
            CreatedBy           = cmd.CustomerId
        };

        // Stamp the customer's status so the UI can reflect the pending state.
        // customers_status_check: active|blocked|deletion_requested|deleted
        var customer = await _db.Customers.FindAsync([cmd.CustomerId], ct);
        if (customer is not null && customer.Status == "active")
        {
            customer.Status    = "deletion_requested";
            customer.UpdatedAt = now;
            customer.Version++;
        }

        _db.AccountDeletionRequests.Add(e);
        await _db.SaveChangesAsync(ct);

        return new AccountDeletionRequestDto(
            e.Id, e.Status, e.RequestSource, e.Reason,
            e.RequestedAt, e.GracePeriodEndsAt, e.CancelledAt);
    }
}

public sealed record CancelDeletionRequestCommand(Guid CustomerId) : IRequest<AccountDeletionRequestDto?>;

public sealed class CancelDeletionRequestHandler : IRequestHandler<CancelDeletionRequestCommand, AccountDeletionRequestDto?>
{
    private readonly LaundryGharDbContext _db;

    public CancelDeletionRequestHandler(LaundryGharDbContext db) => _db = db;

    public async Task<AccountDeletionRequestDto?> Handle(CancelDeletionRequestCommand cmd, CancellationToken ct)
    {
        var e = await _db.AccountDeletionRequests
            .Where(r => r.CustomerId == cmd.CustomerId && r.Status == "pending")
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

        // No pending request — 404 at the endpoint.
        if (e is null) return null;

        var now = DateTimeOffset.UtcNow;
        e.Status      = "cancelled";
        e.CancelledAt = now;

        // Restore customer status to active if it was stamped deletion_requested.
        var customer = await _db.Customers.FindAsync([cmd.CustomerId], ct);
        if (customer is not null && customer.Status == "deletion_requested")
        {
            customer.Status    = "active";
            customer.UpdatedAt = now;
            customer.Version++;
        }

        await _db.SaveChangesAsync(ct);

        return new AccountDeletionRequestDto(
            e.Id, e.Status, e.RequestSource, e.Reason,
            e.RequestedAt, e.GracePeriodEndsAt, e.CancelledAt);
    }
}

public sealed class CreateAddressValidator : AbstractValidator<CreateMyAddressCommand>
{
    /// <summary>
    /// DEF-033: Mirrors the customer_addresses_label_check DB constraint so callers
    /// receive a 422 with a friendly message instead of a raw 23514 violation.
    /// </summary>
    private static readonly string[] AllowedLabels = ["home", "office", "other"];

    public CreateAddressValidator()
    {
        RuleFor(x => x.Request.Label)
            .NotEmpty()
            .MaximumLength(50)
            .Must(l => AllowedLabels.Contains(l))
            .WithMessage($"label must be one of: {string.Join(", ", AllowedLabels)}.");
        RuleFor(x => x.Request.AddressLine1).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Request.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.State).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.Pincode).NotEmpty().MaximumLength(10);
        RuleFor(x => x.Request.CountryCode).NotEmpty().Length(2);
    }
}

/// <summary>
/// DEF-033: Mirrors the customer_addresses_label_check DB constraint so callers
/// receive a 422 with a friendly message instead of a raw 23514 violation.
/// </summary>
public sealed class UpdateAddressValidator : AbstractValidator<UpdateMyAddressCommand>
{
    private static readonly string[] AllowedLabels = ["home", "office", "other"];

    public UpdateAddressValidator()
    {
        RuleFor(x => x.Request.Label)
            .NotEmpty()
            .MaximumLength(50)
            .Must(l => AllowedLabels.Contains(l))
            .WithMessage($"label must be one of: {string.Join(", ", AllowedLabels)}.");
        RuleFor(x => x.Request.AddressLine1).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Request.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.State).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.Pincode).NotEmpty().MaximumLength(10);
        RuleFor(x => x.Request.CountryCode).NotEmpty().Length(2);
    }
}

public sealed class RegisterDeviceValidator : AbstractValidator<RegisterDeviceCommand>
{
    public RegisterDeviceValidator()
    {
        RuleFor(x => x.Request.DeviceId).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Request.Platform).NotEmpty();
    }
}

/// <summary>
/// DEF-001: Validates request_source against the DB CHECK constraint values
/// (account_deletion_requests_request_source_check).
/// Returns 422 via ValidationPipelineBehavior so the caller gets a friendly error
/// instead of a raw 23514 PostgreSQL constraint violation.
/// </summary>
public sealed class CreateDeletionRequestValidator : AbstractValidator<CreateDeletionRequestCommand>
{
    private static readonly string[] AllowedSources = ["mobile_app", "web", "support", "email", "phone"];

    public CreateDeletionRequestValidator()
    {
        RuleFor(x => x.Request.RequestSource)
            .NotEmpty()
            .Must(s => AllowedSources.Contains(s))
            .WithMessage($"requestSource must be one of: {string.Join(", ", AllowedSources)}.");
    }
}

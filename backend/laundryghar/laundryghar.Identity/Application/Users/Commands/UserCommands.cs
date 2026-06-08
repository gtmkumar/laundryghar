using laundryghar.Identity.Infrastructure.Auth;
using laundryghar.Identity.Infrastructure.Services;
using laundryghar.Utilities.Common;
using MediatR;
using System.Security.Cryptography;

namespace laundryghar.Identity.Application.Users.Commands;

// ─── DTOs ──────────────────────────────────────────────────────────────────

public sealed record UserDto(
    Guid Id, string? Email, string? PhoneE164, string UserType, string Status,
    bool MfaEnabled, DateTimeOffset? LastLoginAt, DateTimeOffset CreatedAt,
    string? FirstName, string? LastName, string? DisplayName,
    string? Designation = null,
    string? EmploymentType = null,
    string? PanNumber = null, string? AadhaarNumberMasked = null,
    string? KycStatus = null, DateTimeOffset? KycVerifiedAt = null,
    string? BankAccountName = null, string? BankAccountNumber = null,
    string? BankIfsc = null, string? UpiId = null);

public sealed record CreateUserRequest(
    string? Email, string? Phone, string UserType,
    string? Password = null,
    string? FirstName = null, string? LastName = null, string? Designation = null);

/// <summary>
/// H3: UserType and Status removed — they are privileged fields.
/// Use POST /deactivate to change status; POST /set-type to change UserType.
/// </summary>
public sealed record UpdateUserRequest(
    string? Email = null,
    string? Phone = null,
    string? FirstName = null,
    string? LastName = null,
    string? Designation = null,
    // Employment & payout details (profile). Send "" to clear, null to leave unchanged.
    string? EmploymentType = null,
    string? PanNumber = null,
    string? AadhaarNumberMasked = null,
    string? KycStatus = null,
    string? BankAccountName = null,
    string? BankAccountNumber = null,
    string? BankIfsc = null,
    string? UpiId = null);

/// <summary>H3: Separate command for changing a user's type; requires users.set_type permission.</summary>
public sealed record SetUserTypeRequest(string NewUserType);
public sealed record SetUserTypeCommand(Guid UserId, SetUserTypeRequest Request, ICurrentUser Actor) : IRequest<bool>;

public sealed record SetPasswordRequest(string NewPassword);

// ─── Queries / Commands ────────────────────────────────────────────────────

public sealed record GetUsersQuery(int Page = 1, int PageSize = 20, string? Status = null, string? UserType = null, string? Search = null) : IRequest<PaginatedList<UserDto>>;
public sealed record GetUserByIdQuery(Guid Id)                                  : IRequest<UserDto?>;
public sealed record CreateUserCommand(CreateUserRequest Request, Guid? ActorId) : IRequest<UserDto>;
public sealed record UpdateUserCommand(Guid Id, UpdateUserRequest Request, Guid? ActorId) : IRequest<UserDto?>;
public sealed record DeactivateUserCommand(Guid Id, Guid? ActorId)              : IRequest<bool>;
public sealed record SetPasswordCommand(Guid UserId, SetPasswordRequest Request, Guid? ActorId) : IRequest<bool>;

// ─── Handlers ─────────────────────────────────────────────────────────────

public sealed class GetUsersHandler : IRequestHandler<GetUsersQuery, PaginatedList<UserDto>>
{
    private readonly LaundryGharDbContext _db;
    public GetUsersHandler(LaundryGharDbContext db) => _db = db;
    public Task<PaginatedList<UserDto>> Handle(GetUsersQuery r, CancellationToken ct)
    {
        var q = _db.Users.AsNoTracking()
            .Include(u => u.Profile)
            .AsQueryable();
        if (!string.IsNullOrEmpty(r.Status))   q = q.Where(u => u.Status   == r.Status);
        if (!string.IsNullOrEmpty(r.UserType)) q = q.Where(u => u.UserType == r.UserType);
        if (!string.IsNullOrEmpty(r.Search))
            q = q.Where(u => (u.Email != null && u.Email.Contains(r.Search))
                           || (u.PhoneE164 != null && u.PhoneE164.Contains(r.Search)));
        return PaginatedList<UserDto>.CreateAsync(
            q.OrderByDescending(u => u.CreatedAt).Select(u => new UserDto(
                u.Id, u.Email, u.PhoneE164, u.UserType, u.Status, u.MfaEnabled, u.LastLoginAt, u.CreatedAt,
                u.Profile != null ? u.Profile.FirstName : null,
                u.Profile != null ? u.Profile.LastName  : null,
                u.Profile != null ? u.Profile.DisplayName : null)),
            r.Page, r.PageSize, ct);
    }
}

public sealed class GetUserByIdHandler : IRequestHandler<GetUserByIdQuery, UserDto?>
{
    private readonly LaundryGharDbContext _db;
    public GetUserByIdHandler(LaundryGharDbContext db) => _db = db;
    public Task<UserDto?> Handle(GetUserByIdQuery r, CancellationToken ct) =>
        _db.Users.AsNoTracking().Include(u => u.Profile)
            .Where(u => u.Id == r.Id)
            .Select(u => new UserDto(
                u.Id, u.Email, u.PhoneE164, u.UserType, u.Status, u.MfaEnabled, u.LastLoginAt, u.CreatedAt,
                u.Profile != null ? u.Profile.FirstName   : null,
                u.Profile != null ? u.Profile.LastName    : null,
                u.Profile != null ? u.Profile.DisplayName : null,
                u.Profile != null ? u.Profile.Designation : null,
                u.Profile != null ? u.Profile.EmploymentType : null,
                u.Profile != null ? u.Profile.PanNumber : null,
                u.Profile != null ? u.Profile.AadhaarNumberMasked : null,
                u.Profile != null ? u.Profile.KycStatus : null,
                u.Profile != null ? u.Profile.KycVerifiedAt : null,
                u.Profile != null ? u.Profile.BankAccountName : null,
                u.Profile != null ? u.Profile.BankAccountNumber : null,
                u.Profile != null ? u.Profile.BankIfsc : null,
                u.Profile != null ? u.Profile.UpiId : null))
            .FirstOrDefaultAsync(ct);
}

public sealed class CreateUserHandler : IRequestHandler<CreateUserCommand, UserDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly IPasswordHasher      _hasher;
    public CreateUserHandler(LaundryGharDbContext db, IPasswordHasher hasher) { _db = db; _hasher = hasher; }

    public async Task<UserDto> Handle(CreateUserCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(cmd.Request.Email) && string.IsNullOrEmpty(cmd.Request.Phone))
            throw new laundryghar.Utilities.Exceptions.ValidationException(
                new Dictionary<string, string[]> { ["identifier"] = ["Either email or phone is required."] });

        var inviteToken = cmd.Request.Password is null
            ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            : null;

        var user = new User
        {
            Id              = Guid.NewGuid(),
            Email           = string.IsNullOrEmpty(cmd.Request.Email)   ? null : cmd.Request.Email,
            PhoneE164       = string.IsNullOrEmpty(cmd.Request.Phone)   ? null : cmd.Request.Phone,
            PasswordHash    = cmd.Request.Password is not null ? _hasher.Hash(cmd.Request.Password) : null,
            UserType        = cmd.Request.UserType,
            Status          = cmd.Request.Password is null ? UserStatus.Invited : UserStatus.Active,
            InvitationToken = inviteToken,
            InvitationSentAt = inviteToken is not null ? DateTimeOffset.UtcNow : null,
            Locale          = "en-IN",
            Timezone        = "Asia/Kolkata",
            FailedAttempts  = 0,
            CreatedAt       = DateTimeOffset.UtcNow,
            UpdatedAt       = DateTimeOffset.UtcNow,
            CreatedBy       = cmd.ActorId,
            Version         = 1
        };
        _db.Users.Add(user);

        if (cmd.Request.FirstName is not null || cmd.Request.LastName is not null || cmd.Request.Designation is not null)
        {
            _db.UserProfiles.Add(new UserProfile
            {
                UserId      = user.Id,
                FirstName   = cmd.Request.FirstName,
                LastName    = cmd.Request.LastName,
                Designation = cmd.Request.Designation,
                Preferences = "{}", Metadata = "{}", Status = "active",
                CreatedAt   = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, CreatedBy = cmd.ActorId
            });
        }

        await _db.SaveChangesAsync(ct);
        return new UserDto(user.Id, user.Email, user.PhoneE164, user.UserType, user.Status,
            user.MfaEnabled, user.LastLoginAt, user.CreatedAt,
            cmd.Request.FirstName, cmd.Request.LastName, null);
    }
}

public sealed class UpdateUserHandler : IRequestHandler<UpdateUserCommand, UserDto?>
{
    private readonly LaundryGharDbContext _db;
    public UpdateUserHandler(LaundryGharDbContext db) => _db = db;
    public async Task<UserDto?> Handle(UpdateUserCommand cmd, CancellationToken ct)
    {
        var user = await _db.Users.Include(u => u.Profile).FirstOrDefaultAsync(u => u.Id == cmd.Id, ct);
        if (user is null) return null;

        var r = cmd.Request;

        // H3: Email/Phone only — Status and UserType are NOT assignable here.
        // Status → use /deactivate. UserType → use /set-type (users.set_type permission).
        if (r.Email is not null) user.Email     = r.Email;
        if (r.Phone is not null) user.PhoneE164 = r.Phone;
        user.UpdatedAt = DateTimeOffset.UtcNow; user.UpdatedBy = cmd.ActorId; user.Version++;

        // Does the request touch any profile-backed field? Employees carry employment + KYC
        // + bank details on the profile, so a person with no profile row needs one created.
        var touchesProfile = r.FirstName is not null || r.LastName is not null || r.Designation is not null
            || r.EmploymentType is not null || r.PanNumber is not null || r.AadhaarNumberMasked is not null
            || r.KycStatus is not null || r.BankAccountName is not null || r.BankAccountNumber is not null
            || r.BankIfsc is not null || r.UpiId is not null;

        var profile = user.Profile;
        if (profile is null && touchesProfile)
        {
            profile = new UserProfile
            {
                UserId = user.Id, Preferences = "{}", Metadata = "{}", Status = "active",
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, CreatedBy = cmd.ActorId,
            };
            _db.UserProfiles.Add(profile);
            user.Profile = profile;
        }

        if (profile is not null && touchesProfile)
        {
            // Empty string clears the field; null leaves it unchanged.
            static string? Norm(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
            if (r.FirstName           is not null) profile.FirstName           = Norm(r.FirstName);
            if (r.LastName            is not null) profile.LastName            = Norm(r.LastName);
            if (r.Designation         is not null) profile.Designation         = Norm(r.Designation);
            if (r.EmploymentType      is not null) profile.EmploymentType      = Norm(r.EmploymentType);
            if (r.PanNumber           is not null) profile.PanNumber           = Norm(r.PanNumber)?.ToUpperInvariant();
            if (r.AadhaarNumberMasked is not null) profile.AadhaarNumberMasked = Norm(r.AadhaarNumberMasked);
            if (r.BankAccountName     is not null) profile.BankAccountName     = Norm(r.BankAccountName);
            if (r.BankAccountNumber   is not null) profile.BankAccountNumber   = Norm(r.BankAccountNumber);
            if (r.BankIfsc            is not null) profile.BankIfsc            = Norm(r.BankIfsc)?.ToUpperInvariant();
            if (r.UpiId               is not null) profile.UpiId               = Norm(r.UpiId);
            if (r.KycStatus is not null)
            {
                var next = Norm(r.KycStatus)?.ToLowerInvariant();
                // Stamp verified-at on the pending→verified transition; clear it otherwise.
                if (next == "verified" && profile.KycStatus != "verified") profile.KycVerifiedAt = DateTimeOffset.UtcNow;
                else if (next != "verified") profile.KycVerifiedAt = null;
                profile.KycStatus = next;
            }
            profile.UpdatedAt = DateTimeOffset.UtcNow; profile.UpdatedBy = cmd.ActorId;
        }

        await _db.SaveChangesAsync(ct);
        return new UserDto(user.Id, user.Email, user.PhoneE164, user.UserType, user.Status,
            user.MfaEnabled, user.LastLoginAt, user.CreatedAt,
            profile?.FirstName, profile?.LastName, profile?.DisplayName,
            profile?.Designation, profile?.EmploymentType, profile?.PanNumber, profile?.AadhaarNumberMasked,
            profile?.KycStatus, profile?.KycVerifiedAt, profile?.BankAccountName, profile?.BankAccountNumber,
            profile?.BankIfsc, profile?.UpiId);
    }
}

/// <summary>
/// H3: Changes a user's type. Only callable by actors whose own type is at the same
/// level or higher than the requested type (prevent self-elevation).
/// Requires permission users.set_type.
/// </summary>
public sealed class SetUserTypeHandler : IRequestHandler<SetUserTypeCommand, bool>
{
    // Priority: lower value = higher privilege. Mirrors the seeded roles.Priority ordering.
    private static readonly Dictionary<string, int> TypePriority = new(StringComparer.OrdinalIgnoreCase)
    {
        [laundryghar.SharedDataModel.Enums.UserType.PlatformAdmin]     = 10,
        [laundryghar.SharedDataModel.Enums.UserType.BrandAdmin]        = 20,
        [laundryghar.SharedDataModel.Enums.UserType.FranchiseOwner]    = 40,
        [laundryghar.SharedDataModel.Enums.UserType.StoreAdmin]        = 50,
        [laundryghar.SharedDataModel.Enums.UserType.Staff]             = 60,
        [laundryghar.SharedDataModel.Enums.UserType.WarehouseStaff]    = 80,
        [laundryghar.SharedDataModel.Enums.UserType.Rider]             = 90,
        [laundryghar.SharedDataModel.Enums.UserType.Auditor]           = 100,
        [laundryghar.SharedDataModel.Enums.UserType.Support]           = 110,
    };

    private readonly LaundryGharDbContext _db;
    public SetUserTypeHandler(LaundryGharDbContext db) => _db = db;

    public async Task<bool> Handle(SetUserTypeCommand cmd, CancellationToken ct)
    {
        var actor = cmd.Actor;

        // Only platform_admin can assign platform_admin type
        if (cmd.Request.NewUserType == laundryghar.SharedDataModel.Enums.UserType.PlatformAdmin
            && actor.UserType != laundryghar.SharedDataModel.Enums.UserType.PlatformAdmin)
        {
            throw new UnauthorizedAccessException(
                "Only a platform_admin may assign the platform_admin user type.");
        }

        // Actor's own type must have equal or higher privilege than the type being assigned
        var actorPriority  = TypePriority.GetValueOrDefault(actor.UserType ?? string.Empty, int.MaxValue);
        var targetPriority = TypePriority.GetValueOrDefault(cmd.Request.NewUserType, int.MaxValue);

        if (targetPriority < actorPriority)
        {
            throw new UnauthorizedAccessException(
                "You cannot assign a user type with higher privileges than your own.");
        }

        var user = await _db.Users.FindAsync([cmd.UserId], ct);
        if (user is null) return false;

        user.UserType  = cmd.Request.NewUserType;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        user.UpdatedBy = actor.UserId;
        user.Version++;

        await _db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class DeactivateUserHandler : IRequestHandler<DeactivateUserCommand, bool>
{
    private readonly LaundryGharDbContext _db;
    public DeactivateUserHandler(LaundryGharDbContext db) => _db = db;
    public async Task<bool> Handle(DeactivateUserCommand cmd, CancellationToken ct)
    {
        var user = await _db.Users.FindAsync([cmd.Id], ct);
        if (user is null) return false;
        user.Status    = UserStatus.Suspended;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        user.UpdatedBy = cmd.ActorId;
        user.Version++;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class SetPasswordHandler : IRequestHandler<SetPasswordCommand, bool>
{
    private readonly LaundryGharDbContext _db;
    private readonly IPasswordHasher      _hasher;
    public SetPasswordHandler(LaundryGharDbContext db, IPasswordHasher hasher) { _db = db; _hasher = hasher; }
    public async Task<bool> Handle(SetPasswordCommand cmd, CancellationToken ct)
    {
        var user = await _db.Users.FindAsync([cmd.UserId], ct);
        if (user is null) return false;
        user.PasswordHash      = _hasher.Hash(cmd.Request.NewPassword);
        user.PasswordChangedAt = DateTimeOffset.UtcNow;
        user.MustChangePassword = false;
        user.UpdatedAt         = DateTimeOffset.UtcNow;
        user.UpdatedBy         = cmd.ActorId;
        user.Version++;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

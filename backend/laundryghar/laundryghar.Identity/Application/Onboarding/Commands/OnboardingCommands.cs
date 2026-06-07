using System.Text.Json;
using laundryghar.Identity.Application.AccessControl.Commands;
using laundryghar.Identity.Application.AccessControl.Dtos;
using laundryghar.Identity.Application.Onboarding.Dtos;
using laundryghar.Identity.Application.Onboarding.Queries;
using laundryghar.Identity.Application.TenancyOrg.Commands;
using laundryghar.Identity.Application.Users.Commands;
using laundryghar.Identity.Infrastructure.Services;
using laundryghar.SharedDataModel.Entities.TenancyOrg;
using laundryghar.SharedDataModel.Persistence;
using laundryghar.Utilities.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace laundryghar.Identity.Application.Onboarding.Commands;

// ── Start: create a draft franchise in 'setup' ──────────────────────────────
public sealed record StartOnboardingCommand(StartOnboardingRequest Request, ICurrentUser Actor) : IRequest<OnboardingStateDto>;

public sealed class StartOnboardingHandler : IRequestHandler<StartOnboardingCommand, OnboardingStateDto>
{
    private readonly LaundryGharDbContext _db;
    public StartOnboardingHandler(LaundryGharDbContext db) => _db = db;

    public async Task<OnboardingStateDto> Handle(StartOnboardingCommand cmd, CancellationToken ct)
    {
        var r = cmd.Request;
        if (string.IsNullOrWhiteSpace(r.LegalName))
            throw new ValidationException(new Dictionary<string, string[]> { ["legalName"] = ["Legal name is required."] });
        if (string.IsNullOrWhiteSpace(r.ContactPhone))
            throw new ValidationException(new Dictionary<string, string[]> { ["contactPhone"] = ["Contact phone is required."] });

        var brandId = await OnboardingHelpers.ResolveBrandIdAsync(cmd.Actor, _db, ct)
            ?? throw new ValidationException(new Dictionary<string, string[]> { ["brand"] = ["No brand available."] });

        var now = DateTimeOffset.UtcNow;
        var f = new Franchise
        {
            Id = Guid.NewGuid(), BrandId = brandId,
            Code = await OnboardingHelpers.UniqueFranchiseCodeAsync(_db, brandId, ct),
            LegalName = r.LegalName.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(r.DisplayName) ? null : r.DisplayName.Trim(),
            ContactPhone = r.ContactPhone.Trim(),
            ContactEmail = string.IsNullOrWhiteSpace(r.ContactEmail) ? null : r.ContactEmail.Trim(),
            BillingAddress = "{}", Config = "{}", Metadata = "{}",
            RoyaltyPercent = 0, MarketingFeePercent = 0,
            OnboardingStatus = "setup", Status = "active", Version = 1,
            CreatedAt = now, UpdatedAt = now, CreatedBy = cmd.Actor.UserId, UpdatedBy = cmd.Actor.UserId,
        };
        _db.Franchises.Add(f);
        await _db.SaveChangesAsync(ct);
        return await OnboardingState.BuildAsync(_db, f, ct);
    }
}

// ── Step 1: business & KYC details ──────────────────────────────────────────
public sealed record SaveDetailsCommand(Guid FranchiseId, SaveDetailsRequest Request, ICurrentUser Actor) : IRequest<OnboardingStateDto?>;

public sealed class SaveDetailsHandler : IRequestHandler<SaveDetailsCommand, OnboardingStateDto?>
{
    private readonly LaundryGharDbContext _db;
    public SaveDetailsHandler(LaundryGharDbContext db) => _db = db;

    public async Task<OnboardingStateDto?> Handle(SaveDetailsCommand cmd, CancellationToken ct)
    {
        var f = await _db.Franchises.FirstOrDefaultAsync(x => x.Id == cmd.FranchiseId && x.DeletedAt == null, ct);
        if (f is null) return null;
        var r = cmd.Request;
        if (string.IsNullOrWhiteSpace(r.LegalName))
            throw new ValidationException(new Dictionary<string, string[]> { ["legalName"] = ["Legal name is required."] });

        f.LegalName = r.LegalName.Trim();
        f.DisplayName = string.IsNullOrWhiteSpace(r.DisplayName) ? null : r.DisplayName.Trim();
        f.Gstin = Norm(r.Gstin);
        f.Pan = Norm(r.Pan);
        if (!string.IsNullOrWhiteSpace(r.ContactPhone)) f.ContactPhone = r.ContactPhone.Trim();
        f.ContactEmail = string.IsNullOrWhiteSpace(r.ContactEmail) ? null : r.ContactEmail.Trim();
        f.BillingAddress = Serialize(r.BillingAddress) ?? "{}";
        f.OperationalAddress = Serialize(r.OperationalAddress);
        Touch(f, cmd.Actor);
        await _db.SaveChangesAsync(ct);
        return await OnboardingState.BuildAsync(_db, f, ct);
    }

    private static string? Norm(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().ToUpperInvariant();
    private static string? Serialize(OnboardingAddress? a) =>
        a is null ? null : JsonSerializer.Serialize(new { line1 = a.Line1 ?? "", city = a.City ?? "", state = a.State ?? "", pincode = a.Pincode ?? "" });
    private static void Touch(Franchise f, ICurrentUser u) { f.UpdatedAt = DateTimeOffset.UtcNow; f.UpdatedBy = u.UserId; f.Version++; }
}

// ── Step 2: commercials + agreement ─────────────────────────────────────────
public sealed record SaveCommercialsCommand(Guid FranchiseId, SaveCommercialsRequest Request, ICurrentUser Actor) : IRequest<OnboardingStateDto?>;

public sealed class SaveCommercialsHandler : IRequestHandler<SaveCommercialsCommand, OnboardingStateDto?>
{
    private readonly LaundryGharDbContext _db;
    public SaveCommercialsHandler(LaundryGharDbContext db) => _db = db;

    public async Task<OnboardingStateDto?> Handle(SaveCommercialsCommand cmd, CancellationToken ct)
    {
        var f = await _db.Franchises.FirstOrDefaultAsync(x => x.Id == cmd.FranchiseId && x.DeletedAt == null, ct);
        if (f is null) return null;
        var r = cmd.Request;
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var term = r.TermYears <= 0 ? (short)5 : (short)r.TermYears;

        f.RoyaltyPercent = r.RoyaltyPercent;
        f.MarketingFeePercent = r.MarketingFeePercent;

        FranchiseAgreement agreement;
        if (f.FranchiseAgreementId is Guid aid &&
            await _db.FranchiseAgreements.FirstOrDefaultAsync(a => a.Id == aid, ct) is { } existing)
        {
            agreement = existing;
            agreement.RoyaltyPercent = r.RoyaltyPercent;
            agreement.MarketingFeePercent = r.MarketingFeePercent;
            agreement.InitialFranchiseFee = r.InitialFranchiseFee;
            agreement.TermYears = term;
            agreement.EffectiveTo = today.AddYears(term);
            agreement.UpdatedAt = now; agreement.UpdatedBy = cmd.Actor.UserId; agreement.Version++;
        }
        else
        {
            agreement = new FranchiseAgreement
            {
                Id = Guid.NewGuid(), BrandId = f.BrandId,
                AgreementNumber = $"AGR-{f.Code}-{today.Year}",
                AgreementType = "unit",
                FranchiseeLegalName = f.LegalName,
                FranchiseePan = f.Pan, FranchiseeGstin = f.Gstin, FranchiseePhone = f.ContactPhone, FranchiseeEmail = f.ContactEmail,
                InitialFranchiseFee = r.InitialFranchiseFee,
                RoyaltyPercent = r.RoyaltyPercent, MarketingFeePercent = r.MarketingFeePercent, TechnologyFeeMonthly = 0,
                TermYears = term, RenewalOption = true, ExclusivityClause = true, MinimumStores = 1,
                SlaTerms = "{}", EffectiveFrom = today, EffectiveTo = today.AddYears(term),
                Status = "draft", Version = 1,
                CreatedAt = now, UpdatedAt = now, CreatedBy = cmd.Actor.UserId, UpdatedBy = cmd.Actor.UserId,
            };
            _db.FranchiseAgreements.Add(agreement);
            f.FranchiseAgreementId = agreement.Id;
        }

        f.UpdatedAt = now; f.UpdatedBy = cmd.Actor.UserId; f.Version++;
        await _db.SaveChangesAsync(ct);
        return await OnboardingState.BuildAsync(_db, f, ct);
    }
}

// ── Step 3: franchise owner (invite new or link existing) ───────────────────
public sealed record InviteOwnerCommand(Guid FranchiseId, InviteOwnerRequest Request, ICurrentUser Actor) : IRequest<OnboardingStateDto?>;

public sealed class InviteOwnerHandler : IRequestHandler<InviteOwnerCommand, OnboardingStateDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ISender _sender;
    public InviteOwnerHandler(LaundryGharDbContext db, ISender sender) { _db = db; _sender = sender; }

    public async Task<OnboardingStateDto?> Handle(InviteOwnerCommand cmd, CancellationToken ct)
    {
        var f = await _db.Franchises.FirstOrDefaultAsync(x => x.Id == cmd.FranchiseId && x.DeletedAt == null, ct);
        if (f is null) return null;
        var r = cmd.Request;
        if (string.IsNullOrWhiteSpace(r.Email))
            throw new ValidationException(new Dictionary<string, string[]> { ["email"] = ["Owner email is required."] });

        var roleId = await _db.Roles.AsNoTracking().Where(x => x.Code == "franchise_owner" && x.DeletedAt == null)
            .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct)
            ?? throw new ValidationException(new Dictionary<string, string[]> { ["role"] = ["franchise_owner role is missing."] });

        var email = r.Email.Trim();
        var existing = await _db.Users.AsNoTracking()
            .Where(u => u.Email == email && u.Status != "deleted").Select(u => u.Id).FirstOrDefaultAsync(ct);

        Guid ownerId;
        if (existing != Guid.Empty)
        {
            // Link the existing user and grant the franchise-owner membership for this franchise.
            ownerId = existing;
            await _sender.Send(new GrantMembershipCommand(
                new GrantMembershipRequest(ownerId, "franchise", f.Id, roleId, IsPrimary: true),
                cmd.Actor.UserId, cmd.Actor), ct);
        }
        else
        {
            var invited = await _sender.Send(new InviteUserCommand(
                new InviteUserRequest(email, r.Phone, r.FirstName, r.LastName,
                    "franchise_owner", roleId, "franchise", f.Id, Password: null),
                cmd.Actor), ct);
            ownerId = invited.Id;
        }

        f.OwnerUserId = ownerId;
        f.UpdatedAt = DateTimeOffset.UtcNow; f.UpdatedBy = cmd.Actor.UserId; f.Version++;
        await _db.SaveChangesAsync(ct);
        return await OnboardingState.BuildAsync(_db, f, ct);
    }
}

// ── Step 4: add a store ─────────────────────────────────────────────────────
public sealed record AddStoreCommand(Guid FranchiseId, AddStoreRequest Request, ICurrentUser Actor) : IRequest<OnboardingStateDto?>;

public sealed class AddStoreHandler : IRequestHandler<AddStoreCommand, OnboardingStateDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ISender _sender;
    public AddStoreHandler(LaundryGharDbContext db, ISender sender) { _db = db; _sender = sender; }

    public async Task<OnboardingStateDto?> Handle(AddStoreCommand cmd, CancellationToken ct)
    {
        var f = await _db.Franchises.AsNoTracking().FirstOrDefaultAsync(x => x.Id == cmd.FranchiseId && x.DeletedAt == null, ct);
        if (f is null) return null;
        var r = cmd.Request;
        if (string.IsNullOrWhiteSpace(r.Name))
            throw new ValidationException(new Dictionary<string, string[]> { ["name"] = ["Store name is required."] });

        var count = await _db.Stores.AsNoTracking().CountAsync(s => s.FranchiseId == f.Id, ct);
        var code = string.IsNullOrWhiteSpace(r.Code) ? $"{f.Code}-S{count + 1:00}" : r.Code.Trim().ToUpperInvariant();

        await _sender.Send(new CreateStoreCommand(
            new CreateStoreRequest(f.BrandId, f.Id, code, r.Name.Trim(),
                r.AddressLine1, r.City, r.State, r.Pincode), cmd.Actor.UserId), ct);

        var fresh = await _db.Franchises.AsNoTracking().FirstAsync(x => x.Id == f.Id, ct);
        return await OnboardingState.BuildAsync(_db, fresh, ct);
    }
}

// ── Go-live: activate (gated on the checklist) ──────────────────────────────
public sealed record ActivateFranchiseCommand(Guid FranchiseId, ICurrentUser Actor) : IRequest<OnboardingStateDto?>;

public sealed class ActivateFranchiseHandler : IRequestHandler<ActivateFranchiseCommand, OnboardingStateDto?>
{
    private readonly LaundryGharDbContext _db;
    public ActivateFranchiseHandler(LaundryGharDbContext db) => _db = db;

    public async Task<OnboardingStateDto?> Handle(ActivateFranchiseCommand cmd, CancellationToken ct)
    {
        var f = await _db.Franchises.FirstOrDefaultAsync(x => x.Id == cmd.FranchiseId && x.DeletedAt == null, ct);
        if (f is null) return null;

        var state = await OnboardingState.BuildAsync(_db, f, ct);
        if (state.IsActive) return state;
        if (!state.CanActivate)
        {
            var pending = string.Join(", ", state.Steps.Where(s => !s.Done).Select(s => s.Title));
            throw new ValidationException(new Dictionary<string, string[]>
                { ["onboarding"] = [$"Complete all steps before going live. Pending: {pending}."] });
        }

        var now = DateTimeOffset.UtcNow;
        f.OnboardingStatus = "active";
        f.OnboardedAt = now;
        f.UpdatedAt = now; f.UpdatedBy = cmd.Actor.UserId; f.Version++;

        // Mark the agreement signed/active at go-live.
        if (f.FranchiseAgreementId is Guid aid &&
            await _db.FranchiseAgreements.FirstOrDefaultAsync(a => a.Id == aid, ct) is { } agreement)
        {
            agreement.Status = "active";
            agreement.SignedAt ??= now;
            agreement.UpdatedAt = now; agreement.UpdatedBy = cmd.Actor.UserId; agreement.Version++;
        }

        await _db.SaveChangesAsync(ct);
        return await OnboardingState.BuildAsync(_db, f, ct);
    }
}

// ── Shared helpers ──────────────────────────────────────────────────────────
public static class OnboardingHelpers
{
    public static async Task<Guid?> ResolveBrandIdAsync(ICurrentUser user, LaundryGharDbContext db, CancellationToken ct)
    {
        if (user.BrandId is Guid b) return b;
        return await db.Brands.AsNoTracking().OrderBy(x => x.CreatedAt).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
    }

    public static async Task<string> UniqueFranchiseCodeAsync(LaundryGharDbContext db, Guid brandId, CancellationToken ct)
    {
        for (var i = 0; i < 20; i++)
        {
            var code = "LGF-" + Guid.NewGuid().ToString("N")[..5].ToUpperInvariant();
            if (!await db.Franchises.AsNoTracking().AnyAsync(f => f.BrandId == brandId && f.Code == code, ct))
                return code;
        }
        return "LGF-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    }
}

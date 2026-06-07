using System.Text.Json;
using laundryghar.Identity.Application.Onboarding.Dtos;
using laundryghar.SharedDataModel.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace laundryghar.Identity.Application.Onboarding.Queries;

public sealed record GetOnboardingStateQuery(Guid FranchiseId) : IRequest<OnboardingStateDto?>;

public sealed class GetOnboardingStateHandler : IRequestHandler<GetOnboardingStateQuery, OnboardingStateDto?>
{
    private readonly LaundryGharDbContext _db;
    public GetOnboardingStateHandler(LaundryGharDbContext db) => _db = db;

    public async Task<OnboardingStateDto?> Handle(GetOnboardingStateQuery q, CancellationToken ct)
    {
        var f = await _db.Franchises.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == q.FranchiseId && x.DeletedAt == null, ct);
        if (f is null) return null;

        return await OnboardingState.BuildAsync(_db, f, ct);
    }
}

/// <summary>Builds onboarding state (including the derived checklist) from real franchise data.</summary>
public static class OnboardingState
{
    public static async Task<OnboardingStateDto> BuildAsync(LaundryGharDbContext db, SharedDataModel.Entities.TenancyOrg.Franchise f, CancellationToken ct)
    {
        var storeCount = await db.Stores.AsNoTracking().CountAsync(s => s.FranchiseId == f.Id && s.DeletedAt == null, ct);

        OnboardingOwnerDto owner;
        if (f.OwnerUserId is Guid ownerId)
        {
            var u = await db.Users.AsNoTracking().Where(x => x.Id == ownerId)
                .Select(x => new { x.Id, x.Email, x.Status }).FirstOrDefaultAsync(ct);
            var name = await db.UserProfiles.AsNoTracking().Where(p => p.UserId == ownerId)
                .Select(p => ((p.FirstName ?? "") + " " + (p.LastName ?? "")).Trim()).FirstOrDefaultAsync(ct);
            owner = new OnboardingOwnerDto(u?.Id, string.IsNullOrWhiteSpace(name) ? null : name, u?.Email, u?.Status);
        }
        else owner = new OnboardingOwnerDto(null, null, null, null);

        string? agreementNumber = null;
        if (f.FranchiseAgreementId is Guid aid)
            agreementNumber = await db.FranchiseAgreements.AsNoTracking().Where(a => a.Id == aid)
                .Select(a => a.AgreementNumber).FirstOrDefaultAsync(ct);

        var billing = ParseAddress(f.BillingAddress);
        var operational = ParseAddress(f.OperationalAddress);

        var detailsDone = !string.IsNullOrWhiteSpace(f.Gstin) && !string.IsNullOrWhiteSpace(f.Pan)
                          && billing is not null && !string.IsNullOrWhiteSpace(billing.Line1);
        var commercialsDone = f.FranchiseAgreementId is not null;
        var ownerDone = f.OwnerUserId is not null;
        var storesDone = storeCount > 0;
        var isActive = f.OnboardingStatus == "active";

        var steps = new List<OnboardingStepDto>
        {
            new("details", "Business & KYC", "Legal name, GSTIN, PAN, contact and address.",
                detailsDone, detailsDone ? $"GSTIN {f.Gstin}" : "Add tax and address details"),
            new("commercials", "Commercials & agreement", "Royalty, marketing fee and franchise agreement.",
                commercialsDone, commercialsDone ? $"{f.RoyaltyPercent:0.##}% royalty · {agreementNumber}" : "Set fees and create agreement"),
            new("owner", "Franchise owner", "Invite or assign the owning user.",
                ownerDone, ownerDone ? (owner.Name ?? owner.Email) : "Invite the owner"),
            new("stores", "First store", "Add at least one operating store.",
                storesDone, storesDone ? $"{storeCount} store{(storeCount == 1 ? "" : "s")}" : "Add a store"),
        };

        var doneCount = steps.Count(s => s.Done);
        var progress = (int)Math.Round(doneCount / (double)steps.Count * 100);
        var canActivate = doneCount == steps.Count && !isActive;

        return new OnboardingStateDto(
            f.Id, f.Code, f.LegalName, f.DisplayName, f.Gstin, f.Pan, f.ContactPhone, f.ContactEmail,
            billing, operational, f.RoyaltyPercent, f.MarketingFeePercent,
            InitialFranchiseFee: 0, TermYears: 0, AgreementCreated: commercialsDone, AgreementNumber: agreementNumber,
            owner, storeCount, f.OnboardingStatus, isActive, progress, canActivate, steps);
    }

    public static OnboardingAddress? ParseAddress(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            string Get(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";
            var line1 = Get("line1"); var city = Get("city"); var state = Get("state"); var pincode = Get("pincode");
            if (line1 == "" && city == "" && state == "" && pincode == "") return null;
            return new OnboardingAddress(line1, city, state, pincode);
        }
        catch (JsonException) { return null; }
    }
}

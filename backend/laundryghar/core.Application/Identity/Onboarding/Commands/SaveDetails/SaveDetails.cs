using System.Text.Json;
using core.Application.Common.Interfaces;
using core.Application.Identity.Onboarding.Dtos;
using core.Application.Identity.Onboarding.Queries.GetOnboardingState;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.TenancyOrg;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Onboarding.Commands.SaveDetails;

// Step 1: business & KYC details.
public sealed record SaveDetailsCommand(Guid FranchiseId, SaveDetailsRequest Request) : ICommand<OnboardingStateDto?>;

public class SaveDetailsCommandHandler : ICommandHandler<SaveDetailsCommand, OnboardingStateDto?>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _actor;

    public SaveDetailsCommandHandler(ICoreDbContext db, ICurrentUser actor) { _db = db; _actor = actor; }

    public async Task<OnboardingStateDto?> HandleAsync(SaveDetailsCommand command, CancellationToken cancellationToken)
    {
        var f = await _db.Franchises.FirstOrDefaultAsync(x => x.Id == command.FranchiseId && x.DeletedAt == null, cancellationToken);
        if (f is null) return null;
        if (!_actor.IsWithinScope(brandId: f.BrandId, franchiseId: f.Id))
            throw new ForbiddenException("This franchise is outside your assigned scope.");
        var r = command.Request;
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
        Touch(f, _actor);
        await _db.SaveChangesAsync(cancellationToken);
        return await OnboardingState.BuildAsync(_db, f, cancellationToken);
    }

    private static string? Norm(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().ToUpperInvariant();
    private static string? Serialize(OnboardingAddress? a) =>
        a is null ? null : JsonSerializer.Serialize(new { line1 = a.Line1 ?? "", city = a.City ?? "", state = a.State ?? "", pincode = a.Pincode ?? "" });
    private static void Touch(Franchise f, ICurrentUser u) { f.UpdatedAt = DateTimeOffset.UtcNow; f.UpdatedBy = u.UserId; f.Version++; }
}

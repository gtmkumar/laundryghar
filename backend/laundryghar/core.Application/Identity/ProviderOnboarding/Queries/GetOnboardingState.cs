using System.Text.Json;
using core.Application.Common.Interfaces;
using core.Application.Identity.ProviderOnboarding.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.SharedDataModel.Entities.TenancyOrg;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.ProviderOnboarding.Queries;

public sealed record GetOnboardingStateQuery(Guid BrandId) : IQuery<OnboardingStateDto?>;

/// <summary>
/// §9's wizard, computed rather than remembered.
///
/// <para>Every step's status comes from the account's real contents: locations exist or they do not,
/// the catalogue has items or it does not. The alternative — a <c>completed_steps</c> list the API
/// appends to — is the design that lies, because a provider can finish "add your first location",
/// delete it, and be shown a finished setup over a business that cannot take an order.</para>
///
/// <para>Resume falls out of that for free: "where was I" is always "the first step that is neither
/// done nor skipped", recomputed each time. There is no cursor to get out of sync.</para>
/// </summary>
public sealed class GetOnboardingStateQueryHandler
    : IQueryHandler<GetOnboardingStateQuery, OnboardingStateDto?>
{
    private readonly ICoreDbContext _db;
    private readonly IOnboardingFactsStore _facts;

    public GetOnboardingStateQueryHandler(ICoreDbContext db, IOnboardingFactsStore facts)
    {
        _db = db;
        _facts = facts;
    }

    public async Task<OnboardingStateDto?> HandleAsync(GetOnboardingStateQuery query, CancellationToken ct)
    {
        var facts = await _facts.GetAsync(query.BrandId, ct);
        if (facts is null) return null;

        var row = await _db.OnboardingProgresses.AsNoTracking()
            .FirstOrDefaultAsync(p => p.BrandId == query.BrandId, ct);

        var skipped = new HashSet<string>(row?.SkippedSteps ?? [], StringComparer.Ordinal);

        var steps = new List<OnboardingStepDto>
        {
            Step(OnboardingStep.Location, "Add your first location",
                 facts.Locations > 0,
                 facts.Locations > 0 ? $"{facts.Locations} location(s)" : "No locations yet",
                 skipped),

            Step(OnboardingStep.Catalog, "Set up what you sell",
                 facts.CatalogItems > 0,
                 facts.CatalogItems > 0
                     ? $"{facts.CatalogItems} item(s) — seeded from your template, edit the prices"
                     : "No items yet",
                 skipped),

            Step(OnboardingStep.Staff, "Invite your team",
                 // The founder's own membership does not count as a team; see the SQL function.
                 facts.StaffMembers > 1,
                 facts.StaffMembers > 1 ? $"{facts.StaffMembers} people" : "Just you so far",
                 skipped),

            Step(OnboardingStep.Payments, "Connect online payments",
                 facts.HasGateway,
                 facts.HasGateway ? "Connected" : "Not connected — you can still take cash",
                 skipped),

            Step(OnboardingStep.GoLive, "Go live",
                 facts.PrimaryDomain is not null,
                 facts.PrimaryDomain ?? "Not live yet",
                 skipped),
        };

        var current = steps.FirstOrDefault(s => s.Status == StepStatus.Todo)?.Key;
        var complete = current is null;

        // Stamp the finish time the first time it is observed, and only then. Recording it inside a
        // read is unusual, but the alternative is either a job that polls every brand or a number
        // that is simply never captured — and "how long did onboarding take" is one of the few
        // metrics that tells us whether §9 actually works.
        if (complete && row is { CompletedAt: null })
        {
            var tracked = await _db.OnboardingProgresses.FirstOrDefaultAsync(p => p.BrandId == query.BrandId, ct);
            if (tracked is { CompletedAt: null })
            {
                tracked.CompletedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(ct);
                row = tracked;
            }
        }

        using var draft = JsonDocument.Parse(row?.Draft ?? "{}");

        return new OnboardingStateDto(
            steps, current, complete, facts.PrimaryDomain, facts.OwnFranchiseId,
            row?.StartedAt ?? DateTimeOffset.UtcNow, row?.CompletedAt,
            draft.RootElement.Clone());
    }

    private static OnboardingStepDto Step(
        string key, string title, bool done, string detail, HashSet<string> skipped)
    {
        var skippable = OnboardingStep.Skippable.Contains(key);

        // Done beats skipped. Someone who skipped "invite your team" and later invited someone has
        // done it, and showing that step as skipped would be describing their intention rather than
        // their account.
        var status = done ? StepStatus.Done
                   : skipped.Contains(key) && skippable ? StepStatus.Skipped
                   : StepStatus.Todo;

        return new OnboardingStepDto(key, title, status, detail, skippable);
    }
}

public static class StepStatus
{
    public const string Done = "done";
    public const string Skipped = "skipped";
    public const string Todo = "todo";
}

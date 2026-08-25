namespace core.Application.Identity.ProviderOnboarding.Dtos;

/// <summary>
/// One step of §9's PROVIDER wizard.
///
/// <para>Note the namespace. <c>Identity.Onboarding</c> is already taken by FRANCHISE onboarding —
/// a brand signing up one of its own franchisees (AddStore / InviteOwner / SaveCommercials). This is
/// the other direction: a provider signing up to the platform. Two different actors, two different
/// flows, and folding them together would confuse both.</para>
/// </summary>
/// <param name="Status">done | skipped | todo. Derived from the account's real contents for every
/// value except <c>skipped</c>, which is the one thing the data cannot reveal.</param>
/// <param name="Detail">What is actually there — "2 locations", "no items yet". A wizard that says
/// only "incomplete" makes the user go and look; this saves them the trip.</param>
public sealed record OnboardingStepDto(
    string Key, string Title, string Status, string Detail, bool Skippable);

/// <param name="CurrentStep">Where to resume: the first step that is neither done nor skipped. Null
/// once everything is settled.</param>
/// <param name="Draft">Half-typed answers, keyed by step.</param>
/// <param name="OwnFranchiseId">The id the "add your first location" step needs. Handed over here
/// because `franchises` is an Enterprise feature, so a Starter provider is correctly refused the
/// franchise list — and would otherwise be unable to complete step one.</param>
public sealed record OnboardingStateDto(
    IReadOnlyList<OnboardingStepDto> Steps,
    string? CurrentStep,
    bool Complete,
    string? PrimaryDomain,
    Guid? OwnFranchiseId,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    System.Text.Json.JsonElement Draft);

/// <param name="Draft">Whatever the form holds right now. Stored verbatim under the step's key.</param>
public sealed record SaveOnboardingDraftRequest(string Step, System.Text.Json.JsonElement Draft);

public sealed record SkipOnboardingStepRequest(string Step);

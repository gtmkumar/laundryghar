using System.Text.Json;
using core.Application.Common.Interfaces;
using core.Application.Identity.ProviderOnboarding.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.SharedDataModel.Entities.TenancyOrg;
using laundryghar.Utilities.Auth.Audit;
using laundryghar.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.ProviderOnboarding.Commands;

public sealed record SaveOnboardingDraftCommand(
    Guid BrandId, SaveOnboardingDraftRequest Request, Guid? ActorId) : ICommand<bool>;

/// <summary>
/// Stores half-typed answers so closing the tab does not lose them — the "resumable" half of §9's
/// wizard that genuinely needs storage. Nothing here decides whether a step is complete; that is
/// derived from the account's real contents.
/// </summary>
public sealed class SaveOnboardingDraftCommandHandler : ICommandHandler<SaveOnboardingDraftCommand, bool>
{
    /// <summary>Cap on the stored draft. A wizard form is a handful of fields; anything larger is
    /// either a mistake or someone using the column as storage.</summary>
    public const int MaxDraftBytes = 32 * 1024;

    private readonly ICoreDbContext _db;
    public SaveOnboardingDraftCommandHandler(ICoreDbContext db) => _db = db;

    public async Task<bool> HandleAsync(SaveOnboardingDraftCommand cmd, CancellationToken ct)
    {
        var step = cmd.Request.Step;
        if (!OnboardingStep.All.Contains(step))
            throw new ValidationException(new Dictionary<string, string[]>
            { ["step"] = [$"Unknown step. Expected one of: {string.Join(", ", OnboardingStep.All)}."] });

        var row = await EnsureRowAsync(_db, cmd.BrandId, cmd.ActorId, ct);

        var draft = JsonNodeFrom(row.Draft);
        draft[step] = System.Text.Json.Nodes.JsonNode.Parse(cmd.Request.Draft.GetRawText());

        var serialised = draft.ToJsonString();
        if (System.Text.Encoding.UTF8.GetByteCount(serialised) > MaxDraftBytes)
            throw new ValidationException(new Dictionary<string, string[]>
            { ["draft"] = ["That is too much to hold as a draft. Save the step instead."] });

        row.Draft = serialised;
        row.UpdatedBy = cmd.ActorId;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static System.Text.Json.Nodes.JsonObject JsonNodeFrom(string json)
    {
        try
        {
            return System.Text.Json.Nodes.JsonNode.Parse(json) as System.Text.Json.Nodes.JsonObject ?? [];
        }
        catch (JsonException)
        {
            // A corrupt draft is a lost convenience, never a blocked wizard.
            return [];
        }
    }

    internal static async Task<OnboardingProgress> EnsureRowAsync(
        ICoreDbContext db, Guid brandId, Guid? actorId, CancellationToken ct)
    {
        var row = await db.OnboardingProgresses.FirstOrDefaultAsync(p => p.BrandId == brandId, ct);
        if (row is not null) return row;

        var now = DateTimeOffset.UtcNow;
        row = new OnboardingProgress
        {
            BrandId = brandId, SkippedSteps = [], Draft = "{}",
            StartedAt = now, UpdatedAt = now, CreatedBy = actorId, UpdatedBy = actorId,
        };
        db.OnboardingProgresses.Add(row);
        await db.SaveChangesAsync(ct);
        return row;
    }
}

public sealed record SkipOnboardingStepCommand(
    Guid BrandId, SkipOnboardingStepRequest Request, Guid? ActorId) : ICommand<bool>;

/// <summary>
/// "Not now." The only wizard state that cannot be inferred from the data.
///
/// <para>Only <see cref="OnboardingStep.Skippable"/> steps accept this. A business with no location
/// and no catalogue cannot take a single order, so letting someone skip past those would produce a
/// "finished" setup over an account that does nothing — and going live is not skippable because it
/// IS the finish line.</para>
/// </summary>
public sealed class SkipOnboardingStepCommandHandler : ICommandHandler<SkipOnboardingStepCommand, bool>
{
    private readonly ICoreDbContext _db;
    public SkipOnboardingStepCommandHandler(ICoreDbContext db) => _db = db;

    public async Task<bool> HandleAsync(SkipOnboardingStepCommand cmd, CancellationToken ct)
    {
        var step = cmd.Request.Step;
        if (!OnboardingStep.Skippable.Contains(step))
            throw new ValidationException(new Dictionary<string, string[]>
            { ["step"] = [$"This step cannot be skipped. Skippable: {string.Join(", ", OnboardingStep.Skippable)}."] });

        var row = await SaveOnboardingDraftCommandHandler.EnsureRowAsync(_db, cmd.BrandId, cmd.ActorId, ct);
        if (row.SkippedSteps.Contains(step)) return true;

        row.SkippedSteps = [.. row.SkippedSteps, step];
        row.UpdatedBy = cmd.ActorId;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed record GoLiveCommand(Guid BrandId, Guid? ActorId) : ICommand<string?>;

/// <summary>
/// §9's finish line: "live on sub-domain in minutes."
///
/// <para>Creates <c>&lt;brand-code&gt;.&lt;base&gt;</c>, verified and primary, with no DNS challenge —
/// the zone is ours, and demanding proof that we control our own zone would be ceremony that turns
/// "minutes" into "whenever DNS propagates". The TXT-challenge flow still governs domains the
/// PROVIDER owns, where proving control is the whole point.</para>
///
/// <para>Idempotent, and it never displaces a custom domain the provider already went live on.</para>
/// </summary>
public sealed class GoLiveCommandHandler : ICommandHandler<GoLiveCommand, string?>
{
    /// <summary>Fallback when nothing is configured. Overridden by <c>Onboarding:BaseDomain</c>.</summary>
    public const string DefaultBaseDomain = "laundryghar.app";

    private readonly IOnboardingFactsStore _facts;
    private readonly Microsoft.Extensions.Configuration.IConfiguration _config;
    private readonly IAuditWriter _audit;

    public GoLiveCommandHandler(
        IOnboardingFactsStore facts,
        Microsoft.Extensions.Configuration.IConfiguration config,
        IAuditWriter audit)
    {
        _facts = facts;
        _config = config;
        _audit = audit;
    }

    public async Task<string?> HandleAsync(GoLiveCommand cmd, CancellationToken ct)
    {
        var baseDomain = _config["Onboarding:BaseDomain"] ?? DefaultBaseDomain;

        var domain = await _facts.EnsureSubdomainAsync(cmd.BrandId, baseDomain, ct);
        if (domain is null) return null;

        await _audit.WriteAsync("brand.went_live", "brand", cmd.BrandId, domain,
            newValues: new { domain }, ct: ct);

        return domain;
    }
}

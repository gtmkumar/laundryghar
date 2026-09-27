using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace laundryghar.Utilities.Authorization.Abac;

/// <summary>Hands the PDP the policies that could bear on one question.</summary>
public interface IPolicyRepository
{
    Task<IReadOnlyList<AbacPolicy>> GetAsync(string resourceType, string action, CancellationToken ct);

    /// <summary>Drop the cached set now — called after a policy write in this process so the author
    /// sees their own change immediately rather than up to a window later.</summary>
    void Invalidate();
}

/// <summary>
/// The policy cache (A3.3).
///
/// <para><b>Why a 15-second window and not a long one with push invalidation.</b> The existing
/// authority layer already has a staleness bound: <c>perm_version</c> changes propagate on the next
/// token refresh, which is about 15 seconds. Choosing the same window here means a policy edit and a
/// permission edit become visible on the same timescale, so there is one number to reason about
/// when answering "how long until my change takes effect?" rather than two. A longer window with
/// cross-process invalidation would need a bus the deployment does not have — the output cache is
/// in-process and single-instance today for exactly this reason.</para>
///
/// <para><b>What happens when the load fails.</b> The previous good set is served if there is one,
/// and only an empty list if there is not. An empty list makes every decision NotApplicable, which
/// the PDP treats as deny — so a database outage denies rather than admits. The failure is logged
/// at Error because a silently empty policy set would otherwise look exactly like "no policies have
/// been authored yet".</para>
/// </summary>
public sealed class PolicyCache : IPolicyRepository
{
    /// <summary>Matches the perm_version propagation bound. See the class remarks.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(15);

    private const string CacheKey = "abac:policies:all";

    private readonly IPolicySource _source;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PolicyCache> _log;

    // Last known-good set, used when a refresh throws. Volatile because it is written by whichever
    // request wins the refresh and read by all the others.
    private volatile PolicyIndex? _lastGood;

    public PolicyCache(IPolicySource source, IMemoryCache cache, ILogger<PolicyCache> log)
    {
        _source = source;
        _cache = cache;
        _log = log;
    }

    public async Task<IReadOnlyList<AbacPolicy>> GetAsync(
        string resourceType, string action, CancellationToken ct)
    {
        var index = await GetIndexAsync(ct);
        return index.For(resourceType, action);
    }

    public void Invalidate()
    {
        _cache.Remove(CacheKey);
        _lastGood = null;
    }

    private async Task<PolicyIndex> GetIndexAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(CacheKey, out PolicyIndex? cached) && cached is not null)
            return cached;

        try
        {
            var policies = await _source.LoadAllAsync(ct);
            var index = PolicyIndex.Build(policies);
            _cache.Set(CacheKey, index, Window);
            _lastGood = index;
            return index;
        }
        catch (Exception e)
        {
            _log.LogError(e,
                "ABAC policy load failed; serving {Count} policies from the last known-good set. " +
                "An empty set denies every decision.",
                _lastGood?.Count ?? 0);
            return _lastGood ?? PolicyIndex.Empty;
        }
    }

    /// <summary>
    /// Policies bucketed by (resource_type, action) so a decision is a dictionary hit rather than a
    /// scan of the whole set. Immutable once built, so it can be shared across requests without a
    /// lock.
    /// </summary>
    internal sealed class PolicyIndex
    {
        private readonly Dictionary<(string, string), IReadOnlyList<AbacPolicy>> _byTarget;

        public static readonly PolicyIndex Empty = new(new(), 0);

        public int Count { get; }

        private PolicyIndex(
            Dictionary<(string, string), IReadOnlyList<AbacPolicy>> byTarget, int count)
        {
            _byTarget = byTarget;
            Count = count;
        }

        public static PolicyIndex Build(IReadOnlyList<AbacPolicy> policies)
        {
            var byTarget = policies
                .GroupBy(p => (
                    p.ResourceType.ToLowerInvariant(),
                    p.Action.ToLowerInvariant()))
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<AbacPolicy>)g.OrderBy(p => p.Priority).ToList());

            return new PolicyIndex(byTarget, policies.Count);
        }

        public IReadOnlyList<AbacPolicy> For(string resourceType, string action)
            => _byTarget.TryGetValue(
                (resourceType.ToLowerInvariant(), action.ToLowerInvariant()), out var hit)
                ? hit
                : [];
    }
}

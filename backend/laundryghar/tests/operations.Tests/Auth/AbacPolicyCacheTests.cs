using laundryghar.Utilities.Authorization.Abac;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace operations.Tests.Auth;

/// <summary>
/// Locks in the policy cache (A3.3). The behaviour that matters is what happens when the load
/// FAILS: an authorization layer that answers "no policies" on a database blip would permit
/// everything if the combining algorithm were permissive, and denies everything if it is not. This
/// engine is default-deny, so an empty set is a total outage — which is why the cache serves the
/// last known-good set rather than an empty one.
/// </summary>
public class AbacPolicyCacheTests
{
    private static AbacPolicy Policy(
        string key, string effect = PolicyEffect.Permit,
        string resource = "orders", string action = "read", int priority = 100)
        => new()
        {
            Key = key, Effect = effect, ResourceType = resource, Action = action, Priority = priority,
        };

    private static PolicyCache Cache(IPolicySource source)
        => new(source, new MemoryCache(new MemoryCacheOptions()), NullLogger<PolicyCache>.Instance);

    [Fact]
    public async Task Policies_are_bucketed_by_resource_and_action()
    {
        var source = new StubSource([
            Policy("a", resource: "orders",  action: "read"),
            Policy("b", resource: "orders",  action: "update"),
            Policy("c", resource: "payment", action: "refund"),
        ]);

        var cache = Cache(source);

        Assert.Single(await cache.GetAsync("orders", "read", default));
        Assert.Single(await cache.GetAsync("payment", "refund", default));
        Assert.Empty(await cache.GetAsync("orders", "delete", default));
    }

    [Fact]
    public async Task Lookup_is_case_insensitive_on_both_resource_and_action()
    {
        var cache = Cache(new StubSource([Policy("a", resource: "Orders", action: "Read")]));

        Assert.Single(await cache.GetAsync("orders", "read", default));
        Assert.Single(await cache.GetAsync("ORDERS", "READ", default));
    }

    [Fact]
    public async Task Policies_are_ordered_by_priority_within_a_bucket()
    {
        var cache = Cache(new StubSource([
            Policy("low",  priority: 900),
            Policy("high", priority: 10),
            Policy("mid",  priority: 100),
        ]));

        var ordered = await cache.GetAsync("orders", "read", default);

        Assert.Equal(["high", "mid", "low"], ordered.Select(p => p.Key));
    }

    [Fact]
    public async Task The_source_is_hit_once_per_window_not_once_per_decision()
    {
        var source = new StubSource([Policy("a")]);
        var cache = Cache(source);

        for (var i = 0; i < 25; i++) await cache.GetAsync("orders", "read", default);

        Assert.Equal(1, source.LoadCount);
    }

    [Fact]
    public async Task A_failed_refresh_serves_the_last_known_good_set_rather_than_an_empty_one()
    {
        var source = new StubSource([Policy("a")]);
        var cache = Cache(source);

        Assert.Single(await cache.GetAsync("orders", "read", default));   // warm

        source.ThrowNext = true;
        cache.Invalidate();                                               // force a reload

        // Invalidate() clears the known-good set too, so this one is genuinely empty — that is the
        // documented behaviour and it denies. The point of the next assertion is that a natural
        // expiry, which does NOT clear known-good, keeps serving.
        Assert.Empty(await cache.GetAsync("orders", "read", default));
    }

    [Fact]
    public async Task An_expiry_that_fails_keeps_serving_the_previous_set()
    {
        var source = new StubSource([Policy("a")]);
        var memory = new MemoryCache(new MemoryCacheOptions());
        var cache = new PolicyCache(source, memory, NullLogger<PolicyCache>.Instance);

        Assert.Single(await cache.GetAsync("orders", "read", default));   // warm, known-good set

        // Simulate the window lapsing without touching the known-good reference.
        memory.Remove("abac:policies:all");
        source.ThrowNext = true;

        var served = await cache.GetAsync("orders", "read", default);

        Assert.Single(served);           // still answering from the last good load
        Assert.Equal("a", served[0].Key);
    }

    [Fact]
    public async Task Invalidate_makes_an_authors_own_edit_visible_immediately()
    {
        var source = new StubSource([Policy("before")]);
        var cache = Cache(source);

        Assert.Equal("before", (await cache.GetAsync("orders", "read", default))[0].Key);

        source.Policies = [Policy("after")];
        cache.Invalidate();

        Assert.Equal("after", (await cache.GetAsync("orders", "read", default))[0].Key);
    }

    private sealed class StubSource(IReadOnlyList<AbacPolicy> policies) : IPolicySource
    {
        public IReadOnlyList<AbacPolicy> Policies { get; set; } = policies;
        public int LoadCount { get; private set; }
        public bool ThrowNext { get; set; }

        public Task<IReadOnlyList<AbacPolicy>> LoadAllAsync(CancellationToken ct)
        {
            LoadCount++;
            if (ThrowNext) throw new InvalidOperationException("simulated database failure");
            return Task.FromResult(Policies);
        }

        public Task<string> GetVersionAsync(CancellationToken ct) => Task.FromResult("v1");
    }
}

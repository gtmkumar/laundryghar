namespace laundryghar.Orders.Tests.Pricing;

/// <summary>
/// Pure-logic unit tests for the scope-priority chain in
/// <see cref="laundryghar.Orders.Application.Common.PriceResolver"/>.
///
/// The resolution rule (from the resolver's code comments):
///   Priority: store(2) > franchise(1) > brand(0)
///   Within the same scope, most-recently-published list wins.
///   Row match: brand + service + item; variant-specific row preferred over null-variant row.
///
/// These tests do NOT call PriceResolver.ResolveAsync (which requires EF Core + DB).
/// Instead they mirror the exact LINQ ordering and filtering logic from the resolver
/// to pin the priority semantics as pure in-memory tests.
///
/// Rationale for this approach: the DB-call variant requires testcontainers / a live
/// DB, which is out of scope for this task. The priority ordering is pure C# and
/// can be fully verified without I/O.
/// </summary>
public sealed class PriceResolverScopeChainTests
{
    // ─── Simulated price-list row (mirrors the anonymous type in the resolver) ─────

    private record PriceListEntry(
        Guid   Id,
        string ScopeType,    // "store" | "franchise" | "brand"
        Guid?  FranchiseId,
        Guid?  StoreId,
        DateTimeOffset PublishedAt);

    // ─── Resolution logic mirrored from PriceResolver ────────────────────────────

    private static Guid? ResolveBestListId(
        IEnumerable<PriceListEntry> publishedLists,
        Guid storeId,
        Guid? franchiseId)
    {
        var scopeToRank = new Func<string, int>(scope =>
            scope == "store"     ? 2 :
            scope == "franchise" ? 1 : 0);

        var filtered = publishedLists
            .Where(pl =>
                (pl.ScopeType == "store"     && pl.StoreId     == storeId)     ||
                (pl.ScopeType == "franchise"  && franchiseId.HasValue && pl.FranchiseId == franchiseId) ||
                pl.ScopeType == "brand")
            .OrderByDescending(pl => scopeToRank(pl.ScopeType))
            .ThenByDescending(pl => pl.PublishedAt)
            .Select(pl => pl.Id)
            .ToList();

        return filtered.FirstOrDefault();
    }

    // ─── IDs for test scenarios ───────────────────────────────────────────────────

    private static readonly Guid BrandListId     = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid FranchiseListId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid StoreListId     = Guid.Parse("cccccccc-0000-0000-0000-000000000003");
    private static readonly Guid OlderListId     = Guid.Parse("dddddddd-0000-0000-0000-000000000004");

    private static readonly Guid StoreId1    = Guid.NewGuid();
    private static readonly Guid FranchiseId1 = Guid.NewGuid();

    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    // ─── Scope priority tests ─────────────────────────────────────────────────────

    [Fact]
    public void StoreScope_TakesPriorityOver_FranchiseAndBrand()
    {
        var lists = new[]
        {
            new PriceListEntry(BrandListId,     "brand",     null,        null,    T0),
            new PriceListEntry(FranchiseListId, "franchise", FranchiseId1, null,   T1),
            new PriceListEntry(StoreListId,     "store",     null,        StoreId1, T2),
        };

        var result = ResolveBestListId(lists, StoreId1, FranchiseId1);

        Assert.Equal(StoreListId, result);
    }

    [Fact]
    public void FranchiseScope_TakesPriorityOver_Brand_WhenNoStoreList()
    {
        var lists = new[]
        {
            new PriceListEntry(BrandListId,     "brand",     null,        null,     T0),
            new PriceListEntry(FranchiseListId, "franchise", FranchiseId1, null,    T1),
        };

        var result = ResolveBestListId(lists, StoreId1, FranchiseId1);

        Assert.Equal(FranchiseListId, result);
    }

    [Fact]
    public void BrandScope_UsedAs_FinalFallback_WhenNeitherStoreNorFranchiseMatch()
    {
        var otherStore    = Guid.NewGuid();
        var otherFranchise = Guid.NewGuid();

        var lists = new[]
        {
            new PriceListEntry(BrandListId,     "brand",     null,         null,       T0),
            // store and franchise lists belong to a different store/franchise
            new PriceListEntry(StoreListId,     "store",     null,         otherStore,  T2),
            new PriceListEntry(FranchiseListId, "franchise", otherFranchise, null,      T1),
        };

        // StoreId1 with FranchiseId1 don't match any store/franchise list
        var result = ResolveBestListId(lists, StoreId1, FranchiseId1);

        Assert.Equal(BrandListId, result);
    }

    [Fact]
    public void NoMatchingList_ReturnsEmptyGuid()
    {
        // When no published lists exist, the LINQ chain produces an empty sequence.
        // FirstOrDefault<Guid>() on an empty sequence returns Guid.Empty (the struct default).
        // PriceResolver.ResolveAsync handles this by iterating scopedIds — the loop body
        // is never entered, and the method returns null. This test pins the helper's
        // list-is-empty contract at the pure-C# level.
        var result = ResolveBestListId(Array.Empty<PriceListEntry>(), StoreId1, FranchiseId1);

        Assert.Equal(Guid.Empty, result);
    }

    // ─── Within-scope, most-recently-published wins ───────────────────────────────

    [Fact]
    public void TwoStoreLists_MostRecentPublishedWins()
    {
        var newerListId = Guid.NewGuid();

        var lists = new[]
        {
            new PriceListEntry(OlderListId,  "store", null, StoreId1, T1),
            new PriceListEntry(newerListId,  "store", null, StoreId1, T2),  // newer
        };

        var result = ResolveBestListId(lists, StoreId1, null);

        Assert.Equal(newerListId, result);
    }

    [Fact]
    public void TwoBrandLists_MostRecentPublishedWins()
    {
        var newerBrandId = Guid.NewGuid();

        var lists = new[]
        {
            new PriceListEntry(BrandListId,  "brand", null, null, T0),
            new PriceListEntry(newerBrandId, "brand", null, null, T1),  // newer
        };

        var result = ResolveBestListId(lists, StoreId1, null);

        Assert.Equal(newerBrandId, result);
    }

    // ─── Store list beats older store list AND newer franchise/brand ─────────────

    [Fact]
    public void OlderStoreList_StillBeats_NewerFranchiseAndBrandLists()
    {
        // Scope rank (2) always wins over franchise (1) and brand (0),
        // regardless of PublishedAt ordering within the SAME scope tier.
        var lists = new[]
        {
            new PriceListEntry(StoreListId,     "store",     null,         StoreId1,    T0),  // oldest
            new PriceListEntry(FranchiseListId, "franchise", FranchiseId1, null,        T2),  // newest
            new PriceListEntry(BrandListId,     "brand",     null,         null,        T1),
        };

        var result = ResolveBestListId(lists, StoreId1, FranchiseId1);

        // Store scope MUST win even though its PublishedAt is older
        Assert.Equal(StoreListId, result);
    }

    // ─── Store for a different store is not returned for this store ──────────────

    [Fact]
    public void StoreList_ForDifferentStore_IsNotReturned()
    {
        var differentStoreId = Guid.NewGuid();
        var lists = new[]
        {
            new PriceListEntry(BrandListId,  "brand", null,             null,            T0),
            new PriceListEntry(StoreListId,  "store", null,             differentStoreId, T2),
        };

        // StoreId1 is not differentStoreId, so the store list must not match
        var result = ResolveBestListId(lists, StoreId1, null);

        // Must fall back to brand
        Assert.Equal(BrandListId, result);
    }

    // ─── FranchiseId null means franchise-scoped lists are excluded ──────────────

    [Fact]
    public void NullFranchiseId_ExcludesFranchiseScopedLists()
    {
        var lists = new[]
        {
            new PriceListEntry(BrandListId,     "brand",     null,        null, T0),
            new PriceListEntry(FranchiseListId, "franchise", FranchiseId1, null, T1),
        };

        // Passing null franchiseId means the store has no franchise
        var result = ResolveBestListId(lists, StoreId1, franchiseId: null);

        // Franchise list must not be selected when franchiseId is null
        Assert.Equal(BrandListId, result);
        Assert.NotEqual(FranchiseListId, result);
    }
}

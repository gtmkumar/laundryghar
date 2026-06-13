using laundryghar.Engagement.Application.Cms.Queries;

namespace laundryghar.Engagement.Tests.Cms;

/// <summary>
/// DEF-5 regression: archived (soft-deleted) notification templates must NOT appear in
/// the default list, but an explicit status filter can surface them.
/// </summary>
public sealed class NotificationTemplateListFilterTests
{
    // ── Default (no filter) excludes archived ──────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DefaultFilter_ExcludesArchived(string? filter)
    {
        Assert.False(NotificationTemplateListFilter.ShouldInclude(filter, "archived"));
        Assert.True(NotificationTemplateListFilter.ShouldInclude(filter, "active"));
    }

    // ── "all" includes everything ──────────────────────────────────────────────

    [Theory]
    [InlineData("all")]
    [InlineData("ALL")]
    [InlineData(" All ")]
    public void AllSentinel_IncludesArchivedAndActive(string filter)
    {
        Assert.True(NotificationTemplateListFilter.ShouldInclude(filter, "archived"));
        Assert.True(NotificationTemplateListFilter.ShouldInclude(filter, "active"));
    }

    // ── Specific status → exact match ──────────────────────────────────────────

    [Fact]
    public void ArchivedFilter_ReturnsOnlyArchived()
    {
        Assert.True(NotificationTemplateListFilter.ShouldInclude("archived", "archived"));
        Assert.False(NotificationTemplateListFilter.ShouldInclude("archived", "active"));
    }

    [Fact]
    public void ActiveFilter_ReturnsOnlyActive()
    {
        Assert.True(NotificationTemplateListFilter.ShouldInclude("active", "active"));
        Assert.False(NotificationTemplateListFilter.ShouldInclude("active", "archived"));
    }
}

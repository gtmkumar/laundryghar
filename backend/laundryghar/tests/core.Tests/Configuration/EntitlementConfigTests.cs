using System.Text.Json;
using Xunit;

namespace core.Tests.Configuration;

/// <summary>
/// Pins the shipped value of <c>Entitlement:Enforced</c>.
///
/// <para>This is a config test rather than a behaviour test on purpose. The entitlement filter was
/// fully implemented in <c>ScopeResolver</c> and <c>GetNavigator</c> for months and did nothing at
/// all, because the key appeared in no appsettings file and <c>GetValue&lt;bool&gt;</c> quietly
/// returned false. Nothing failed; the feature simply was not on. A test that asserts the switch is
/// ON is the only thing that catches that class of regression — an accidental revert would otherwise
/// disable every entitlement in the platform without a single test going red.</para>
///
/// <para>Safe to enable because migration 0008 backfilled every existing brand from its plan and
/// grandfathered the remainder, and the migration itself aborts unless no brand loses a module.</para>
/// </summary>
public class EntitlementConfigTests
{
    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Development.json")]
    public void Entitlement_enforcement_is_switched_on(string fileName)
    {
        var path = Path.Combine(RepoRoot(), "backend", "laundryghar", "core.WebApi", fileName);
        Assert.True(File.Exists(path), $"{fileName} not found at {path}");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));

        Assert.True(
            doc.RootElement.TryGetProperty("Entitlement", out var entitlement),
            $"{fileName} has no Entitlement section — the key must be EXPLICIT, never an implicit false.");

        Assert.True(
            entitlement.TryGetProperty("Enforced", out var enforced),
            $"{fileName} Entitlement section has no Enforced key.");

        Assert.True(
            enforced.GetBoolean(),
            $"{fileName} has Entitlement:Enforced = false. Every feature entitlement in the platform " +
            "is inert while this is off. If it was turned off deliberately, change this test and say why.");
    }

    /// <summary>Walks up from the test assembly to the directory holding db/migrations.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "db", "migrations"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("could not locate the repo root from the test assembly");
    }
}

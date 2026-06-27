using System.Reflection;

namespace operations.IntegrationTests;

/// <summary>Locates repo-relative assets (SQL patches) from the test assembly location.</summary>
internal static class RepoPaths
{
    /// <summary>Walks up from the test bin dir until it finds the directory containing db/patches.</summary>
    public static string RepoRoot { get; } = FindRoot();

    public static string Patch(string fileName)
        => Path.Combine(RepoRoot, "db", "patches", fileName);

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "db", "patches")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repo root (db/patches) from the test assembly path.");
    }
}

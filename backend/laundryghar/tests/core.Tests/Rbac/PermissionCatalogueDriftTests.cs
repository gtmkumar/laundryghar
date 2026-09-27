using System.Reflection;
using System.Text.RegularExpressions;
using core.Infrastructure.Seeders;
using Xunit;

namespace core.Tests.Rbac;

/// <summary>
/// Audit finding A-5, "catalogue drift".
///
/// <para><b>What was wrong.</b> A permission code can enter the platform two ways: declared in
/// <see cref="IdentitySeeder"/>'s <c>PermissionDefs</c>, or inserted by a SQL migration or patch.
/// Nothing required the second to be folded back into the first, so seven codes —
/// <c>impersonation.request</c>, <c>impersonation.approve</c>, <c>api_keys.manage</c>,
/// <c>domains.read</c>, <c>domains.manage</c>, <c>white_label.read</c> and
/// <c>dispatch.mode.manage</c> — existed in a live database while the C# catalogue said the
/// platform had 164 permissions. A database built from the seeder alone was missing all seven.</para>
///
/// <para><b>Why a test rather than another fix.</b> This had happened at least twice before: the
/// <c>R3-SEC-1</c> comments in <c>PermissionDefs</c> record earlier rounds of the same drift being
/// reconciled by hand, each time after someone noticed. Noticing is the part that keeps failing, so
/// the rule is asserted here instead.</para>
///
/// <para>No database is involved — this reads the SQL files that ship in the repo, which is what
/// makes it a build-time guard rather than something that only fails once an environment has
/// already drifted.</para>
/// </summary>
public sealed class PermissionCatalogueDriftTests
{
    [Fact]
    public void Every_permission_code_inserted_by_SQL_is_declared_in_the_seeder_catalogue()
    {
        var declared = IdentitySeeder.PermissionDefs.Select(p => p.Code).ToHashSet(StringComparer.Ordinal);
        var inSql = CodesInsertedBySql();

        // A non-empty parse is part of the assertion: if the scanner silently stopped matching (a
        // file renamed, an INSERT reworded), an empty set would make this test pass forever.
        Assert.NotEmpty(inSql);

        var missing = inSql.Except(declared).Order().ToList();
        Assert.True(missing.Count == 0,
            "These permission codes are inserted by a SQL file but are not declared in "
            + $"IdentitySeeder.PermissionDefs, so a database seeded from C# alone would not have them:{Environment.NewLine}"
            + string.Join(Environment.NewLine, missing.Select(c => "  - " + c)));
    }

    [Fact]
    public void The_seeder_catalogue_declares_no_duplicate_codes()
    {
        var dupes = IdentitySeeder.PermissionDefs
            .GroupBy(p => p.Code, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .Order()
            .ToList();

        // SeedPermissionsAsync keys existing rows by code, so a duplicate here is silently
        // idempotent at runtime and invisible until two entries disagree on risk level.
        Assert.True(dupes.Count == 0, "Duplicate codes in PermissionDefs: " + string.Join(", ", dupes));
    }

    [Fact]
    public void Every_declared_risk_level_satisfies_the_database_check_constraint()
    {
        // identity_access.permissions carries
        //   CHECK (risk_level = ANY (ARRAY['low','normal','high','critical']))
        // and the seeder inserts these strings verbatim, so a typo here is not a compile error —
        // it is a 23514 that aborts seeding on a fresh environment, long after the commit.
        string[] allowed = ["low", "normal", "high", "critical"];

        var bad = IdentitySeeder.PermissionDefs
            .Where(p => !allowed.Contains(p.Risk, StringComparer.Ordinal))
            .Select(p => $"{p.Code} → '{p.Risk}'")
            .Order()
            .ToList();

        Assert.True(bad.Count == 0,
            "risk_level outside the table's check constraint:" + Environment.NewLine
            + string.Join(Environment.NewLine, bad.Select(b => "  - " + b)));
    }

    // ── the scanner ─────────────────────────────────────────────────────────────────────────────

    private static readonly Regex InsertHeader = new(
        @"INSERT\s+INTO\s+identity_access\.permissions\s*\(([^)]*)\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Every literal written into the <c>code</c> column of <c>identity_access.permissions</c> by a
    /// SQL file in the repo.
    ///
    /// <para>The column is located by name in the INSERT's column list and then read POSITIONALLY
    /// out of each row. Matching quoted strings that merely look like codes does not work: the
    /// <c>action</c> column legitimately holds values such as <c>booking.create</c> and
    /// <c>invoice.read</c> (the partner_* family splits its codes that way), and a loose regex reads
    /// those as codes that were never declared.</para>
    ///
    /// <para>Both INSERT shapes used in this repo are handled: <c>VALUES (…), (…)</c> and
    /// <c>SELECT …  WHERE NOT EXISTS (…)</c>.</para>
    /// </summary>
    private static HashSet<string> CodesInsertedBySql()
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in SqlFiles())
        {
            var text = File.ReadAllText(file);

            foreach (Match header in InsertHeader.Matches(text))
            {
                var columns = header.Groups[1].Value
                    .Split(',')
                    .Select(c => c.Trim().ToLowerInvariant())
                    .ToList();

                var codeIndex = columns.IndexOf("code");
                if (codeIndex < 0) continue;

                var rest = text[header.Index..];
                rest = rest[(header.Length)..];

                foreach (var row in RowExpressions(rest))
                {
                    var fields = SplitTopLevel(row);
                    if (codeIndex >= fields.Count) continue;

                    var literal = Regex.Match(fields[codeIndex].Trim(), @"^'([^']*)'$");
                    if (literal.Success) codes.Add(literal.Groups[1].Value);
                }
            }
        }

        return codes;
    }

    /// <summary>The comma-separated field lists of each row an INSERT supplies — the contents of
    /// each <c>(…)</c> tuple after VALUES, or the single projection list after SELECT.</summary>
    private static IEnumerable<string> RowExpressions(string afterColumns)
    {
        var values = Regex.Match(afterColumns, @"^\s*VALUES\s*", RegexOptions.IgnoreCase);
        if (values.Success)
        {
            var rest = afterColumns[values.Length..];
            var depth = 0;
            var start = -1;

            for (var i = 0; i < rest.Length; i++)
            {
                var c = rest[i];
                if (c == '(')
                {
                    if (depth == 0) start = i + 1;
                    depth++;
                }
                else if (c == ')')
                {
                    depth--;
                    if (depth == 0 && start >= 0)
                    {
                        yield return rest[start..i];
                        // Another tuple follows only if the next non-space character is a comma.
                        var tail = rest[(i + 1)..];
                        if (!Regex.IsMatch(tail, @"^\s*,")) yield break;
                    }
                }
                else if (depth == 0 && c == ';') yield break;
            }
            yield break;
        }

        var select = Regex.Match(afterColumns, @"^\s*SELECT\s+", RegexOptions.IgnoreCase);
        if (!select.Success) yield break;

        var body = afterColumns[select.Length..];
        var terminator = Regex.Match(body, @"\n\s*(WHERE|FROM|ON\s+CONFLICT|;)", RegexOptions.IgnoreCase);
        yield return terminator.Success ? body[..terminator.Index] : body;
    }

    /// <summary>Splits on commas that are not inside parentheses or a quoted string — so a
    /// description containing a comma, or a <c>jsonb_build_object(a, b)</c> call, stays one field
    /// and every later field keeps its position.</summary>
    private static List<string> SplitTopLevel(string s)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var depth = 0;
        var quoted = false;

        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];

            if (quoted)
            {
                current.Append(c);
                if (c != '\'') continue;
                // '' inside a string literal is an escaped quote, not the end of it.
                if (i + 1 < s.Length && s[i + 1] == '\'') { current.Append(s[++i]); continue; }
                quoted = false;
                continue;
            }

            switch (c)
            {
                case '\'': quoted = true; current.Append(c); break;
                case '(':  depth++;       current.Append(c); break;
                case ')':  depth--;       current.Append(c); break;
                case ',' when depth == 0: fields.Add(current.ToString().Trim()); current.Clear(); break;
                default:                  current.Append(c); break;
            }
        }

        if (current.ToString().Trim().Length > 0) fields.Add(current.ToString().Trim());
        return fields;
    }

    private static IEnumerable<string> SqlFiles()
    {
        var root = RepoRoot();
        foreach (var dir in new[] { Path.Combine(root, "db"), Path.Combine(root, "database_scripts") })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.EnumerateFiles(dir, "*.sql", SearchOption.AllDirectories))
                yield return f;
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "db", "patches"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repo root (db/patches) from the test assembly path.");
    }
}

using System.Text;
using System.Text.RegularExpressions;

namespace PMDA.Api.Database;

/// <summary>One registered flight database. The password is DPAPI-protected (base64), never plain text.</summary>
public sealed record DatabaseEntry(
    string Id,
    string Name,
    string Server,
    string Database,
    DatabaseAuthentication Authentication,
    string? User,
    string? ProtectedPassword,
    bool Encrypt,
    bool TrustServerCertificate,
    bool ReadOnly,
    bool Enabled);

/// <summary>Every flight database PMDA works with, and the one imports go to unless the upload says otherwise.</summary>
public sealed record DatabaseRegistry(string? DefaultImportId, IReadOnlyList<DatabaseEntry> Databases)
{
    public static readonly DatabaseRegistry Empty = new(null, []);
}

public static partial class DatabaseIds
{
    public const int MaxLength = 32;

    [GeneratedRegex("^[a-z0-9-]{1,32}$")]
    private static partial Regex Valid();

    public static bool IsValid(string? id) => id is not null && Valid().IsMatch(id);

    /// <summary>A short, URL-safe id from a display name ("Archive 2025" → "archive-2025"), unique among <paramref name="taken"/>.</summary>
    public static string FromName(string name, IEnumerable<string> taken)
    {
        var slug = new StringBuilder();
        foreach (var c in name.Trim().ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
                slug.Append(c);
            else if (slug.Length > 0 && slug[^1] != '-')
                slug.Append('-');
        }
        var baseId = slug.ToString().Trim('-');
        if (baseId.Length == 0)
            baseId = "db";
        baseId = baseId[..Math.Min(baseId.Length, MaxLength - 4)].TrimEnd('-');

        var used = taken.ToHashSet(StringComparer.Ordinal);
        var id = baseId;
        for (var n = 2; used.Contains(id); n++)
            id = $"{baseId}-{n}";
        return id;
    }
}

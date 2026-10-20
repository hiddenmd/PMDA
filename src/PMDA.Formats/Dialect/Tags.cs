using System.Globalization;

namespace PMDA.Formats.Dialect;

/// <summary>One <c>&lt;Name&gt;value</c> directive from a declaration's line comment.</summary>
public sealed record Tag(string Name, string Value);

/// <summary>
/// The directives on one declaration. Names compare case-insensitively; when a tag repeats, the first one counts.
/// </summary>
public sealed class Tags
{
    public static readonly Tags Empty = new([]);

    public Tags(IReadOnlyList<Tag> items) => Items = items;

    public IReadOnlyList<Tag> Items { get; }

    public bool Has(string name) => Items.Any(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public string? Get(string name) => Items.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;

    /// <summary>A non-empty value, else null.</summary>
    public string? Value(string name) => Get(name) is { Length: > 0 } v ? v : null;

    public double? Number(string name) =>
        Value(name) is { } v && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    /// <summary><c>&lt;Parse&gt;False</c>: the field takes space in the record but is not decoded.</summary>
    public bool Skipped => Get("Parse") is { } v && v.Equals("False", StringComparison.OrdinalIgnoreCase);

    public Tags Merge(Tags other) => other.Items.Count == 0 ? this : Items.Count == 0 ? other : new([.. Items, .. other.Items]);

    /// <summary>
    /// Reads every <c>&lt;Name&gt;value</c> in a comment. A value is the text after the tag up to the next space or
    /// tag, after any spaces (vendors write <c>&lt;Resolution&gt; 0.005</c>). <c>&lt;bits=1&gt;</c> reads as Name "bits",
    /// value "1". Text that is not a tag is prose and ignored.
    /// </summary>
    public static Tags Parse(string? comment)
    {
        if (string.IsNullOrEmpty(comment))
            return Empty;
        var items = new List<Tag>();
        var i = 0;
        while ((i = comment.IndexOf('<', i)) >= 0)
        {
            var close = comment.IndexOf('>', i + 1);
            if (close < 0)
                break;
            var name = comment[(i + 1)..close].Trim();
            if (name.Length == 0 || !IsTagName(name))
            {
                i++;
                continue;
            }
            var value = "";
            var eq = name.IndexOf('=');
            if (eq > 0)
            {
                value = name[(eq + 1)..].Trim();
                name = name[..eq].Trim();
            }
            var j = close + 1;
            if (eq < 0)
            {
                while (j < comment.Length && comment[j] == ' ')
                    j++;
                var start = j;
                while (j < comment.Length && !char.IsWhiteSpace(comment[j]) && comment[j] != '<')
                    j++;
                value = comment[start..j];
                // "<Debrief>  // prose": a comment marker is not a value.
                if (value.StartsWith("//", StringComparison.Ordinal))
                    value = "";
            }
            items.Add(new Tag(name, value));
            i = j;
        }
        return items.Count == 0 ? Empty : new Tags(items);
    }

    private static bool IsTagName(string s)
    {
        if (!char.IsLetter(s[0]))
            return false;
        foreach (var c in s)
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '=' && c != '.')
                return false;
        return true;
    }
}

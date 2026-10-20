using PMDA.Formats.Dialect;
using PMDA.Formats.Layout;

namespace PMDA.Formats.Decoding;

public enum ColumnType { Int, BigInt, Float, Text }

/// <summary>How generated tables and columns are named. Legacy PMDA names will be matched once its schema is known.</summary>
public sealed record NamingOptions
{
    /// <summary>"Struct" uses the struct name; "Title" uses <c>&lt;Title&gt;</c> when there is one.</summary>
    public string Tables { get; init; } = "Struct";
    /// <summary>"Field" uses the field name; "Title" uses <c>&lt;Title&gt;</c> when there is one.</summary>
    public string Columns { get; init; } = "Field";
    /// <summary>First index in the names of flattened array columns.</summary>
    public int ArrayIndexBase { get; init; }
}

/// <param name="Field">The field path recipes refer to, e.g. "smid_code" or "elint_data_0_eid_ind".</param>
/// <param name="Name">The SQL column name.</param>
public sealed record ColumnDef(string Field, string Name, ColumnType Type, int TextLength, Tags Tags)
{
    public string? Title => Tags.Value("Title");
    public string? Unit => Tags.Value("Unit");
    public string? Enum => Tags.Value("Enum");
    public string? ColorEnum => Tags.Value("ColorEnum");
}

/// <summary>A <c>&lt;SubStructure&gt;count</c> array, stored as rows of a child table.</summary>
public sealed class ChildDef
{
    internal ChildDef(string field, string name, string countField, int maxCount, int stride, IReadOnlyList<ColumnDef> columns, IReadOnlyList<Leaf> leaves)
    {
        Field = field;
        Name = name;
        CountField = countField;
        MaxCount = maxCount;
        Stride = stride;
        Columns = columns;
        Leaves = leaves;
    }

    public string Field { get; }
    /// <summary>Name suffix of the child table.</summary>
    public string Name { get; }
    public string CountField { get; }
    public int MaxCount { get; }
    /// <summary>Bytes per element.</summary>
    public int Stride { get; }
    public IReadOnlyList<ColumnDef> Columns { get; }
    /// <summary>Element fields, with offsets relative to the element.</summary>
    internal IReadOnlyList<Leaf> Leaves { get; }
}

/// <summary>A decodable field: where its bits are and how to turn them into a value.</summary>
internal sealed record Leaf(
    string Field,
    int Offset,
    int UnitSize,
    int BitOffset,
    int BitWidth,
    ScalarKind Kind,
    double? Scale,
    double Bias,
    int TextLength)
{
    public bool IsText => TextLength > 0;
    public int End => Offset + (IsText ? TextLength : UnitSize);
}

/// <summary>A child array as it sits in one particular layout of its struct.</summary>
internal sealed record ChildLeaf(ChildDef Child, int Offset, Leaf? Count);

/// <summary>
/// The columns and child tables of one message type, independent of which fields a raw record actually
/// carries. Built from the full struct; decoders for reduced layouts map into the same columns.
/// </summary>
public sealed class MessagePlan
{
    private readonly Dictionary<string, int> _byField;

    internal MessagePlan(string key, StructDecl decl, string tableName, IReadOnlyList<ColumnDef> columns, IReadOnlyList<ChildDef> children)
    {
        Key = key;
        Decl = decl;
        TableName = tableName;
        Columns = columns;
        Children = children;
        _byField = new(StringComparer.Ordinal);
        for (var i = 0; i < columns.Count; i++)
            _byField.TryAdd(columns[i].Field, i);
    }

    /// <summary>The struct name, or the track name for a derived message.</summary>
    public string Key { get; }
    public StructDecl Decl { get; }
    public string TableName { get; }
    public IReadOnlyList<ColumnDef> Columns { get; }
    public IReadOnlyList<ChildDef> Children { get; }

    public int IndexOf(string field) => _byField.TryGetValue(field, out var i) ? i : -1;

    public ChildDef? Child(string field) => Children.FirstOrDefault(c => c.Field == field);

    public MessagePlan Renamed(string key, string tableName) => new(key, Decl, tableName, Columns, Children);
}

/// <summary>Walks a struct layout into leaves (one per stored value) and child arrays.</summary>
/// <param name="includeSkipped">Also yield <c>&lt;Parse&gt;False</c> scalars, for reading keys and counts that are not stored.</param>
internal sealed class Flattener(NamingOptions naming, bool includeSkipped = false)
{
    public sealed record Result(List<(ColumnDef Column, Leaf Leaf)> Leaves, List<ChildLeaf> Children);

    public Result Flatten(StructLayout layout, bool forChildren = true)
    {
        var result = new Result(new(), new());
        var used = new HashSet<string>(StringComparer.Ordinal);
        Walk(layout, 0, "", Tags.Empty, result, used, forChildren);
        return result;
    }

    private void Walk(StructLayout layout, int baseOffset, string prefix, Tags inherited, Result result, HashSet<string> used, bool children)
    {
        foreach (var m in layout.Members)
        {
            var f = m.Field;
            if (f.IsAnonymous)
                continue;
            var tags = f.Tags.Merge(inherited);
            var name = prefix + f.Name;

            if (f.Tags.Value("SubStructure") is { } countField && f.IsArray)
            {
                // Count fields are decoded even when they are not stored.
                var countLeaf = layout.Members.FirstOrDefault(x => x.Field.Name == countField) is { Scalar: not null } c
                    ? ScalarLeaf(c, baseOffset + c.Offset, countField, Tags.Empty)
                    : null;
                if (children && !f.Tags.Skipped && !includeSkipped)
                    result.Children.Add(new ChildLeaf(Child(m, name, countField, tags), baseOffset + m.Offset, countLeaf));
                continue;
            }
            if (f.Tags.Skipped && !includeSkipped)
                continue;

            if (m.Scalar is { } scalar)
            {
                var isText = scalar.Kind == ScalarKind.Char && f.IsArray && !m.IsBitfield;
                if (isText || !f.IsArray)
                {
                    var leaf = isText
                        ? new Leaf(Unique(name, used), baseOffset + m.Offset, 1, 0, 0, ScalarKind.Char, null, 0, m.Count)
                        : ScalarLeaf(m, baseOffset + m.Offset, Unique(name, used), tags);
                    result.Leaves.Add((Column(leaf, tags, scalar), leaf));
                    continue;
                }
                for (var i = 0; i < m.Count; i++)
                {
                    var leaf = ScalarLeaf(m, baseOffset + m.Offset + i * m.ElementSize, Unique($"{name}_{i + naming.ArrayIndexBase}", used), tags);
                    result.Leaves.Add((Column(leaf, tags, scalar), leaf));
                }
                continue;
            }

            var nested = m.Struct!;
            if (!f.IsArray)
            {
                // Nested structs (record headers) contribute their fields directly; a clash gets the field's name as prefix.
                var clash = nested.Members.Any(x => !x.Field.IsAnonymous && used.Contains(prefix + x.Field.Name));
                Walk(nested, baseOffset + m.Offset, clash ? name + "_" : prefix, tags, result, used, children);
                continue;
            }
            for (var i = 0; i < m.Count; i++)
                Walk(nested, baseOffset + m.Offset + i * m.ElementSize, $"{name}_{i + naming.ArrayIndexBase}_", tags, result, used, children);
        }
    }

    private ChildDef Child(MemberLayout m, string name, string countField, Tags tags)
    {
        var elementTags = tags.Items.Where(t => t.Name is not ("SubStructure" or "Title" or "HelpTopic")).ToList();
        var leaves = new List<(ColumnDef, Leaf)>();
        if (m.Scalar is { } scalar)
        {
            var leaf = ScalarLeaf(m, 0, "value", tags);
            leaves.Add((Column(leaf, new Tags(elementTags), scalar), leaf));
        }
        else
        {
            var inner = new Result(new(), new());
            Walk(m.Struct!, 0, "", new Tags(elementTags), inner, new HashSet<string>(StringComparer.Ordinal), children: false);
            leaves.AddRange(inner.Leaves);
        }
        return new ChildDef(name, name, countField, m.Count, m.ElementSize,
            leaves.Select(l => l.Item1).ToList(), leaves.Select(l => l.Item2).ToList());
    }

    private static Leaf ScalarLeaf(MemberLayout m, int offset, string field, Tags tags)
    {
        var scale = tags.Number("Resolution");
        var bias = tags.Number("Bias");
        return new Leaf(field, offset, m.ElementSize, m.BitOffset, m.IsBitfield ? m.BitWidth : m.ElementSize * 8,
            m.Scalar!.Kind == ScalarKind.Char ? ScalarKind.Signed : m.Scalar.Kind,
            scale is null && bias is null ? null : scale ?? 1, bias ?? 0, 0);
    }

    private ColumnDef Column(Leaf leaf, Tags tags, ScalarType scalar)
    {
        var name = naming.Columns.Equals("Title", StringComparison.OrdinalIgnoreCase) && tags.Value("Title") is { } t ? t : leaf.Field;
        ColumnType type;
        if (leaf.IsText)
            type = ColumnType.Text;
        else if (leaf.Scale is not null || scalar.Kind == ScalarKind.Float)
            type = ColumnType.Float;
        else if (leaf.BitWidth > 32 || (leaf.BitWidth == 32 && scalar.Kind == ScalarKind.Unsigned))
            type = ColumnType.BigInt;
        else
            type = ColumnType.Int;
        return new ColumnDef(leaf.Field, name, type, leaf.TextLength, tags);
    }

    private static string Unique(string name, HashSet<string> used)
    {
        var candidate = name;
        for (var n = 2; !used.Add(candidate); n++)
            candidate = $"{name}_{n}";
        return candidate;
    }
}

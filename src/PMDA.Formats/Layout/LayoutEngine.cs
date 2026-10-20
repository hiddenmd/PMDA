using PMDA.Formats.Dialect;

namespace PMDA.Formats.Layout;

public enum ByteOrder { LittleEndian, BigEndian }

/// <summary>Where a bitfield's first declared field sits in its storage unit.</summary>
public enum BitOrder { LsbFirst, MsbFirst }

/// <summary>
/// Layout rules a C compiler would otherwise decide. Defaults match MSVC on x86: little-endian, bitfields
/// allocated from the least significant bit, natural alignment.
/// </summary>
public sealed record LayoutOptions
{
    public ByteOrder ByteOrder { get; init; } = ByteOrder.LittleEndian;
    public BitOrder BitOrder { get; init; } = BitOrder.LsbFirst;
    /// <summary>Largest alignment, as <c>#pragma pack(n)</c>: 1, 2, 4 or 8.</summary>
    public int Pack { get; init; } = 8;
}

public enum ScalarKind { Unsigned, Signed, Float, Char }

public sealed record ScalarType(string Name, int Size, ScalarKind Kind)
{
    public bool IsSigned => Kind is ScalarKind.Signed;
}

public sealed class StructLayout(StructDecl decl, int size, int align, IReadOnlyList<MemberLayout> members)
{
    public StructDecl Decl { get; } = decl;
    public string Name => Decl.DisplayName;
    public int Size { get; } = size;
    public int Align { get; } = align;
    public IReadOnlyList<MemberLayout> Members { get; } = members;
}

/// <param name="Offset">Byte offset of the member, or of its storage unit for a bitfield.</param>
/// <param name="ElementSize">Size of one element (the storage unit for a bitfield).</param>
/// <param name="Count">Array length, or 1.</param>
/// <param name="BitOffset">Bit position of a bitfield inside its unit, counted from the least significant bit.</param>
/// <param name="BitWidth">0 when the member is not a bitfield.</param>
public sealed record MemberLayout(
    FieldDecl Field,
    int Offset,
    int ElementSize,
    int Count,
    ScalarType? Scalar,
    StructLayout? Struct,
    int BitOffset,
    int BitWidth)
{
    public bool IsBitfield => BitWidth > 0;
    public int End => Offset + ElementSize * Count;
}

/// <summary>Computes byte and bit positions of every field, with the rules in <see cref="LayoutOptions"/>.</summary>
public sealed class LayoutEngine(HeaderModel model, LayoutOptions options)
{
    private readonly Dictionary<string, StructLayout> _cache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _building = new(StringComparer.Ordinal);

    public LayoutOptions Options => options;
    public HeaderModel Model => model;

    /// <summary>The struct's layout; with <paramref name="excluded"/>, as if those top-level fields were not declared.</summary>
    public StructLayout Layout(StructDecl decl, IReadOnlySet<string>? excluded = null)
    {
        var cacheable = excluded is null || excluded.Count == 0;
        var key = decl.DisplayName;
        if (cacheable && _cache.TryGetValue(key, out var cached))
            return cached;
        if (!_building.Add(key))
            throw new FormatException($"struct {key} contains itself.");
        try
        {
            var layout = Build(decl, cacheable ? null : excluded);
            if (cacheable)
                _cache[key] = layout;
            return layout;
        }
        finally
        {
            _building.Remove(key);
        }
    }

    private StructLayout Build(StructDecl decl, IReadOnlySet<string>? excluded)
    {
        var pack = Math.Clamp(options.Pack, 1, 8);
        var members = new List<MemberLayout>();
        int offset = 0, maxAlign = 1;
        int unitStart = -1, unitSize = 0, unitUsed = 0;

        foreach (var f in decl.Fields)
        {
            if (excluded is not null && !f.IsAnonymous && excluded.Contains(f.Name))
                continue;
            var (scalar, nested) = Resolve(f.TypeName, decl.DisplayName);
            if (f.BitWidth is { } w)
            {
                if (scalar is null)
                    throw new FormatException($"{decl.DisplayName}.{HeaderParser.FieldLabel(f)}: a bitfield must have an integer type.");
                var size = scalar.Size;
                if (w > size * 8)
                    throw new FormatException($"{decl.DisplayName}.{HeaderParser.FieldLabel(f)}: {w} bits do not fit in {f.TypeName}.");
                if (w == 0)
                {
                    unitStart = -1;
                    continue;
                }
                if (unitStart < 0 || unitSize != size || unitUsed + w > size * 8)
                {
                    var align = Math.Min(size, pack);
                    offset = AlignUp(offset, align);
                    maxAlign = Math.Max(maxAlign, align);
                    unitStart = offset;
                    unitSize = size;
                    unitUsed = 0;
                    offset += size;
                }
                var bit = options.BitOrder == BitOrder.LsbFirst ? unitUsed : size * 8 - unitUsed - w;
                members.Add(new MemberLayout(f, unitStart, size, 1, scalar, null, bit, w));
                unitUsed += w;
            }
            else
            {
                unitStart = -1;
                var elementSize = scalar?.Size ?? nested!.Size;
                var align = Math.Min(scalar?.Size ?? nested!.Align, pack);
                offset = AlignUp(offset, align);
                maxAlign = Math.Max(maxAlign, align);
                var count = f.IsArray ? f.ArrayLength : 1;
                members.Add(new MemberLayout(f, offset, elementSize, count, scalar, nested, 0, 0));
                offset += elementSize * count;
            }
        }
        return new StructLayout(decl, AlignUp(offset, maxAlign), maxAlign, members);
    }

    private static int AlignUp(int value, int align) => (value + align - 1) / align * align;

    private (ScalarType? Scalar, StructLayout? Struct) Resolve(string typeName, string owner, int depth = 0)
    {
        if (depth > 32)
            throw new FormatException($"Type {typeName} in {owner} is defined in terms of itself.");
        var words = typeName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w is not ("const" or "volatile")).ToList();
        if (words.Count == 2 && words[0] is "struct" or "enum")
            words.RemoveAt(0);
        if (words.Count == 1)
        {
            var name = words[0];
            if (model.Typedefs.TryGetValue(name, out var target))
                return Resolve(target, owner, depth + 1);
            if (model.FindStruct(name) is { } s)
                return (null, Layout(s));
            if (model.Enums.ContainsKey(name))
                return (new ScalarType(name, 4, ScalarKind.Signed), null);
        }
        return (Scalar(words) ?? throw new FormatException($"{owner}: unknown type '{typeName}'."), null);
    }

    private static ScalarType? Scalar(List<string> words)
    {
        var name = string.Join(' ', words);
        var unsigned = words.Contains("unsigned");
        var kind = unsigned ? ScalarKind.Unsigned : ScalarKind.Signed;
        if (words.Contains("float"))
            return new ScalarType(name, 4, ScalarKind.Float);
        if (words.Contains("double"))
            return new ScalarType(name, 8, ScalarKind.Float);
        if (words.Contains("char"))
            return new ScalarType(name, 1, words.Contains("signed") || unsigned ? kind : ScalarKind.Char);
        if (words.Contains("bool") || words.Contains("_Bool") || words.Contains("__int8"))
            return new ScalarType(name, 1, ScalarKind.Unsigned);
        if (words.Contains("short") || words.Contains("__int16"))
            return new ScalarType(name, 2, kind);
        if (words.Contains("__int64") || words.Count(w => w == "long") >= 2)
            return new ScalarType(name, 8, kind);
        // LLP64: long is 32 bits, as on the Windows tools these headers are written for.
        if (words.All(w => w is "unsigned" or "signed" or "int" or "long" or "__int32"))
            return new ScalarType(name, 4, kind);
        return null;
    }
}

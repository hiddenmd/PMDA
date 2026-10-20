using System.Buffers.Binary;
using System.Text;
using PMDA.Formats.Layout;

namespace PMDA.Formats.Decoding;

/// <summary>One decoded record: a value per column of its plan, plus child rows.</summary>
public sealed class DecodedRecord(MessagePlan plan, object?[] values, List<object?[]>?[] children)
{
    public MessagePlan Plan { get; } = plan;
    public object?[] Values { get; } = values;
    /// <summary>Rows per child in <see cref="MessagePlan.Children"/> order; null when the record has none.</summary>
    public List<object?[]>?[] Children { get; } = children;
    /// <summary>The payload ended before the struct did; the missing fields are null.</summary>
    public bool Short { get; init; }

    public object? this[string field] => Plan.IndexOf(field) is var i and >= 0 ? Values[i] : null;
}

/// <summary>Decodes payloads laid out as one struct (possibly with some fields left out) into a plan's columns.</summary>
public sealed class RecordDecoder
{
    private readonly (Leaf Leaf, int Column)[] _leaves;
    private readonly (ChildLeaf Child, int Index)[] _children;
    private readonly ByteOrder _order;
    private readonly Dictionary<string, Leaf> _allLeaves;

    internal RecordDecoder(MessagePlan plan, StructLayout layout, ByteOrder order, Flattener.Result flat, IEnumerable<Leaf> hidden)
    {
        Plan = plan;
        Layout = layout;
        _order = order;
        _leaves = flat.Leaves
            .Select(l => (l.Leaf, plan.IndexOf(l.Leaf.Field)))
            .Where(l => l.Item2 >= 0)
            .ToArray();
        _children = flat.Children
            .Select(c => (c, plan.Children.ToList().FindIndex(p => p.Field == c.Child.Field)))
            .Where(c => c.Item2 >= 0)
            .ToArray();
        _allLeaves = new(StringComparer.Ordinal);
        foreach (var l in flat.Leaves)
            _allLeaves.TryAdd(l.Leaf.Field, l.Leaf);
        foreach (var l in hidden)
            _allLeaves.TryAdd(l.Field, l);
    }

    public MessagePlan Plan { get; }
    public StructLayout Layout { get; }
    public ByteOrder Order => _order;

    internal Leaf? Leaf(string field) => _allLeaves.GetValueOrDefault(field);

    internal (ChildDef Child, int Offset)? ChildAt(string field) =>
        _children.FirstOrDefault(c => c.Child.Child.Field == field) is { Child: { } c } ? (c.Child, c.Offset) : null;

    public DecodedRecord Decode(ReadOnlySpan<byte> payload)
    {
        var values = new object?[Plan.Columns.Count];
        foreach (var (leaf, column) in _leaves)
            values[column] = Value(leaf, payload, 0, _order);
        var children = new List<object?[]>?[Plan.Children.Count];
        var isShort = payload.Length < Layout.Size;
        foreach (var (child, index) in _children)
        {
            var count = child.Count is { } c ? Raw(c, payload, 0, _order) ?? 0 : child.Child.MaxCount;
            count = Math.Clamp(count, 0, child.Child.MaxCount);
            var room = Math.Max(0, (payload.Length - child.Offset) / child.Child.Stride);
            if (count > room)
            {
                count = room;
                isShort = true;
            }
            if (count > 0)
                children[index] = child.Child.DecodeElements(payload[child.Offset..], (int)count, _order);
        }
        return new DecodedRecord(Plan, values, children) { Short = isShort };
    }

    /// <summary>Whether the layout has this field, stored or not.</summary>
    public bool HasField(string field) => _allLeaves.ContainsKey(field);

    /// <summary>The raw integer of any field of the layout, stored or not (counts, keys, lifecycle codes).</summary>
    public long? ReadRaw(ReadOnlySpan<byte> payload, string field) =>
        _allLeaves.TryGetValue(field, out var leaf) ? Raw(leaf, payload, 0, _order) : null;

    internal static object? Value(Leaf leaf, ReadOnlySpan<byte> s, int baseOffset, ByteOrder order)
    {
        var offset = baseOffset + leaf.Offset;
        if (offset + (leaf.IsText ? leaf.TextLength : leaf.UnitSize) > s.Length)
            return null;
        if (leaf.IsText)
        {
            var bytes = s.Slice(offset, leaf.TextLength);
            var zero = bytes.IndexOf((byte)0);
            return Encoding.Latin1.GetString(zero >= 0 ? bytes[..zero] : bytes).TrimEnd();
        }
        if (leaf.Kind == ScalarKind.Float)
        {
            var unit = Unit(s, offset, leaf.UnitSize, order);
            double f = leaf.UnitSize == 4 ? BitConverter.Int32BitsToSingle((int)unit) : BitConverter.Int64BitsToDouble((long)unit);
            return leaf.Scale is { } fs ? f * fs + leaf.Bias : f;
        }
        var raw = Raw(leaf, s, baseOffset, order)!.Value;
        if (leaf.Scale is { } scale)
            return raw * scale + leaf.Bias;
        if (leaf.BitWidth > 32 || (leaf.BitWidth == 32 && leaf.Kind == ScalarKind.Unsigned))
            return raw;
        return (int)raw;
    }

    internal static long? Raw(Leaf leaf, ReadOnlySpan<byte> s, int baseOffset, ByteOrder order)
    {
        var offset = baseOffset + leaf.Offset;
        if (leaf.IsText || offset + leaf.UnitSize > s.Length)
            return null;
        var unit = Unit(s, offset, leaf.UnitSize, order);
        var bits = leaf.BitWidth;
        var v = bits >= 64 ? unit : (unit >> leaf.BitOffset) & ((1UL << bits) - 1);
        if (leaf.Kind == ScalarKind.Signed && bits < 64 && ((v >> (bits - 1)) & 1) == 1)
            return (long)(v | ~((1UL << bits) - 1));
        return (long)v;
    }

    private static ulong Unit(ReadOnlySpan<byte> s, int offset, int size, ByteOrder order)
    {
        var b = s.Slice(offset, size);
        var little = order == ByteOrder.LittleEndian;
        return size switch
        {
            1 => b[0],
            2 => little ? BinaryPrimitives.ReadUInt16LittleEndian(b) : BinaryPrimitives.ReadUInt16BigEndian(b),
            4 => little ? BinaryPrimitives.ReadUInt32LittleEndian(b) : BinaryPrimitives.ReadUInt32BigEndian(b),
            8 => little ? BinaryPrimitives.ReadUInt64LittleEndian(b) : BinaryPrimitives.ReadUInt64BigEndian(b),
            _ => throw new InvalidOperationException($"Unsupported unit size {size}."),
        };
    }
}

public static class ChildDefExtensions
{
    /// <summary>Decodes <paramref name="count"/> consecutive elements from the start of <paramref name="bytes"/>.</summary>
    public static List<object?[]> DecodeElements(this ChildDef child, ReadOnlySpan<byte> bytes, int count, ByteOrder order)
    {
        var rows = new List<object?[]>(count);
        for (var i = 0; i < count && (i + 1) * child.Stride <= bytes.Length; i++)
        {
            var row = new object?[child.Columns.Count];
            for (var j = 0; j < child.Leaves.Count; j++)
                row[j] = RecordDecoder.Value(child.Leaves[j], bytes, i * child.Stride, order);
            rows.Add(row);
        }
        return rows;
    }
}

using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using PMDA.Formats.Layout;

namespace PMDA.Formats.Decoding;

/// <summary>Writes field values into record bytes: the inverse of decoding, for the sample generator and tests.</summary>
internal static class FieldWriter
{
    public static void Write(byte[] buffer, Leaf leaf, long raw, ByteOrder order, int baseOffset = 0)
    {
        var offset = baseOffset + leaf.Offset;
        if (offset + leaf.UnitSize > buffer.Length)
            return;
        var span = buffer.AsSpan(offset, leaf.UnitSize);
        var unit = Read(span, order);
        var bits = leaf.BitWidth;
        var mask = bits >= 64 ? ulong.MaxValue : ((1UL << bits) - 1) << leaf.BitOffset;
        unit = (unit & ~mask) | (((ulong)raw << leaf.BitOffset) & mask);
        WriteUnit(span, unit, order);
    }

    public static void WriteFloat(byte[] buffer, Leaf leaf, double value, ByteOrder order, int baseOffset = 0)
    {
        var span = buffer.AsSpan(baseOffset + leaf.Offset, leaf.UnitSize);
        ulong bits = leaf.UnitSize == 4 ? (uint)BitConverter.SingleToInt32Bits((float)value) : (ulong)BitConverter.DoubleToInt64Bits(value);
        WriteUnit(span, bits, order);
    }

    private static ulong Read(Span<byte> b, ByteOrder order) => (b.Length, order) switch
    {
        (1, _) => b[0],
        (2, ByteOrder.LittleEndian) => BinaryPrimitives.ReadUInt16LittleEndian(b),
        (2, _) => BinaryPrimitives.ReadUInt16BigEndian(b),
        (4, ByteOrder.LittleEndian) => BinaryPrimitives.ReadUInt32LittleEndian(b),
        (4, _) => BinaryPrimitives.ReadUInt32BigEndian(b),
        (8, ByteOrder.LittleEndian) => BinaryPrimitives.ReadUInt64LittleEndian(b),
        _ => BinaryPrimitives.ReadUInt64BigEndian(b),
    };

    private static void WriteUnit(Span<byte> b, ulong v, ByteOrder order)
    {
        switch (b.Length, order)
        {
            case (1, _): b[0] = (byte)v; break;
            case (2, ByteOrder.LittleEndian): BinaryPrimitives.WriteUInt16LittleEndian(b, (ushort)v); break;
            case (2, _): BinaryPrimitives.WriteUInt16BigEndian(b, (ushort)v); break;
            case (4, ByteOrder.LittleEndian): BinaryPrimitives.WriteUInt32LittleEndian(b, (uint)v); break;
            case (4, _): BinaryPrimitives.WriteUInt32BigEndian(b, (uint)v); break;
            case (8, ByteOrder.LittleEndian): BinaryPrimitives.WriteUInt64LittleEndian(b, v); break;
            default: BinaryPrimitives.WriteUInt64BigEndian(b, v); break;
        }
    }

    /// <summary>Writes an engineering value: strings as Latin-1, scaled numbers back to their raw integer.</summary>
    public static void WriteValue(byte[] buffer, Leaf leaf, object value, ByteOrder order, int baseOffset = 0)
    {
        if (leaf.IsText)
        {
            var bytes = Encoding.Latin1.GetBytes(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
            bytes.AsSpan(0, Math.Min(bytes.Length, leaf.TextLength)).CopyTo(buffer.AsSpan(baseOffset + leaf.Offset));
            return;
        }
        var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        if (leaf.Kind == ScalarKind.Float)
        {
            WriteFloat(buffer, leaf, leaf.Scale is { } fs ? (number - leaf.Bias) / fs : number, order, baseOffset);
            return;
        }
        var raw = leaf.Scale is { } scale ? Math.Round((number - leaf.Bias) / scale) : number;
        Write(buffer, leaf, (long)raw, order, baseOffset);
    }
}

/// <summary>Builds record payloads from field values by name.</summary>
public static class RecordEncoder
{
    /// <param name="values">Field → engineering value. Unknown names throw, so tests catch typos.</param>
    /// <param name="children">Child array field → element rows (element field → value).</param>
    /// <param name="length">Payload length; defaults to the struct size.</param>
    public static byte[] Encode(
        this RecordDecoder decoder,
        IReadOnlyDictionary<string, object> values,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object>>>? children = null,
        int? length = null)
    {
        var buffer = new byte[length ?? decoder.Layout.Size];
        var order = decoder.Order;
        foreach (var (field, value) in values)
        {
            var leaf = decoder.Leaf(field) ?? throw new ArgumentException($"{decoder.Layout.Name} has no field '{field}'.", nameof(values));
            FieldWriter.WriteValue(buffer, leaf, value, order);
        }
        foreach (var (field, rows) in children ?? new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object>>>())
        {
            var (child, offset) = decoder.ChildAt(field) ?? throw new ArgumentException($"{decoder.Layout.Name} has no child array '{field}'.", nameof(children));
            for (var i = 0; i < rows.Count && i < child.MaxCount; i++)
                foreach (var (name, value) in rows[i])
                {
                    var index = child.Columns.ToList().FindIndex(c => c.Field == name);
                    if (index < 0)
                        throw new ArgumentException($"{field} elements have no field '{name}'.", nameof(children));
                    FieldWriter.WriteValue(buffer, child.Leaves[index], value, order, offset + i * child.Stride);
                }
        }
        return buffer;
    }

    /// <summary>Encodes raw 32-bit words (an extension record's value list) from child element rows.</summary>
    public static byte[] EncodeElements(this ChildDef child, IReadOnlyList<IReadOnlyDictionary<string, object>> rows, ByteOrder order)
    {
        var buffer = new byte[rows.Count * child.Stride];
        for (var i = 0; i < rows.Count; i++)
            foreach (var (name, value) in rows[i])
            {
                var index = child.Columns.ToList().FindIndex(c => c.Field == name);
                if (index < 0)
                    throw new ArgumentException($"{child.Field} elements have no field '{name}'.", nameof(rows));
                FieldWriter.WriteValue(buffer, child.Leaves[index], value, order, i * child.Stride);
            }
        return buffer;
    }
}

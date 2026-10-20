using System.Globalization;
using System.Text.RegularExpressions;
using PMDA.Core.Parsing;
using PMDA.Formats.Decoding;
using PMDA.Formats.Layout;

namespace PMDA.Formats.Framing;

/// <summary>One record cut out of a raw recorder dump. <see cref="Payload"/> is valid until the reader advances.</summary>
public readonly record struct FramedRecord(int EventId, long Time, long FileOffset, ReadOnlyMemory<byte> Payload);

/// <summary>
/// The record header described by the struct tagged <c>&lt;Header&gt;</c>: <c>&lt;UserEntityID&gt;</c> is the
/// event id, <c>&lt;EventTime&gt;</c> the time and <c>&lt;Length&gt;All|DataOnly</c> the record length, counted in
/// units of <c>&lt;Unit&gt;</c> bits. Leading signature fields are the sync pattern used to find records again.
/// </summary>
public sealed partial class RecordFrame
{
    public const int MaxRecordBytes = 16 << 20;

    private readonly Leaf _eventId, _time, _length;

    private RecordFrame(int headerSize, Leaf eventId, Leaf time, Leaf length, bool lengthIncludesHeader, int lengthUnitBytes, byte[] sync, ByteOrder order)
    {
        HeaderSize = headerSize;
        _eventId = eventId;
        _time = time;
        _length = length;
        LengthIncludesHeader = lengthIncludesHeader;
        LengthUnitBytes = lengthUnitBytes;
        Sync = sync;
        Order = order;
    }

    public int HeaderSize { get; }
    public bool LengthIncludesHeader { get; }
    public int LengthUnitBytes { get; }
    public byte[] Sync { get; }
    public ByteOrder Order { get; }

    internal static RecordFrame Create(StructLayout header, ByteOrder order, string? syncHex, NamingOptions naming)
    {
        var leaves = new Flattener(naming, includeSkipped: true).Flatten(header, forChildren: false).Leaves;
        Leaf Find(string tag) =>
            leaves.FirstOrDefault(l => l.Column.Tags.Has(tag)).Leaf
            ?? throw new FormatException($"no field is tagged <{tag}>.");

        var lengthLeaf = Find("Length");
        var lengthColumn = leaves.First(l => l.Leaf == lengthLeaf).Column;
        var mode = lengthColumn.Tags.Value("Length") ?? "DataOnly";
        var unitBits = lengthColumn.Tags.Number("Unit") ?? 8;
        if (unitBits < 8 || unitBits % 8 != 0)
            throw new FormatException($"<Unit>{unitBits} on the length must be a multiple of 8 bits.");

        byte[] sync;
        if (syncHex is not null)
            sync = Convert.FromHexString(syncHex.Replace(" ", "", StringComparison.Ordinal));
        else
            sync = DeriveSync(header);

        return new RecordFrame(header.Size, Find("UserEntityID"), Find("EventTime"), lengthLeaf,
            mode.Equals("All", StringComparison.OrdinalIgnoreCase), (int)unitBits / 8, sync, order);
    }

    /// <summary>
    /// Leading <c>&lt;Parse&gt;False</c> fields named like <c>_signature5a1</c> hold the byte 0x5A in every byte.
    /// Stops at the first field that does not say what it holds.
    /// </summary>
    private static byte[] DeriveSync(StructLayout header)
    {
        var bytes = new List<byte>();
        foreach (var m in header.Members)
        {
            if (!m.Field.Tags.Skipped || m.IsBitfield || m.Scalar is null)
                break;
            var match = SignatureName().Match(m.Field.Name);
            if (!match.Success)
                break;
            var b = byte.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            for (var i = 0; i < m.ElementSize * m.Count; i++)
                bytes.Add(b);
        }
        return bytes.ToArray();
    }

    [GeneratedRegex(@"signature_?([0-9A-Fa-f]{2})", RegexOptions.IgnoreCase)]
    private static partial Regex SignatureName();

    /// <summary>Reads the header at the start of <paramref name="s"/>. False when the sync bytes do not match.</summary>
    public bool TryRead(ReadOnlySpan<byte> s, out int eventId, out long time, out int payloadLength)
    {
        eventId = 0;
        time = 0;
        payloadLength = 0;
        if (Sync.Length > 0 && !s.StartsWith(Sync))
            return false;
        eventId = (int)(RecordDecoder.Raw(_eventId, s, 0, Order) ?? 0);
        time = RecordDecoder.Raw(_time, s, 0, Order) ?? 0;
        var length = (RecordDecoder.Raw(_length, s, 0, Order) ?? 0) * LengthUnitBytes;
        if (LengthIncludesHeader)
            length -= HeaderSize;
        if (length < 0 || length > MaxRecordBytes)
            return false;
        payloadLength = (int)length;
        return true;
    }

    /// <summary>Writes a record header; used by the sample generator and tests.</summary>
    public byte[] Write(int eventId, long time, int payloadLength)
    {
        var buffer = new byte[HeaderSize];
        Sync.CopyTo(buffer, 0);
        Put(buffer, _eventId, eventId);
        Put(buffer, _time, time);
        var length = payloadLength + (LengthIncludesHeader ? HeaderSize : 0);
        Put(buffer, _length, length / LengthUnitBytes);
        return buffer;
    }

    private void Put(byte[] buffer, Leaf leaf, long value) => FieldWriter.Write(buffer, leaf, value, Order);
}

/// <summary>Reads a raw dump record by record, finding the next sync pattern after damage.</summary>
public static class RecordReader
{
    private const int BufferSize = 1 << 20;

    public static IEnumerable<FramedRecord> Read(Stream stream, RecordFrame frame, IParseDiagnostics diagnostics, CancellationToken ct)
    {
        var buffer = new byte[Math.Max(BufferSize, frame.HeaderSize * 2)];
        int start = 0, end = 0;
        long bufferOffset = stream.CanSeek ? stream.Position : 0;
        var eof = false;
        var lostSync = false;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var available = end - start;
            if (available >= frame.HeaderSize)
            {
                var span = buffer.AsSpan(start, available);
                if (frame.TryRead(span, out var eventId, out var time, out var payloadLength))
                {
                    var total = frame.HeaderSize + payloadLength;
                    if (total > buffer.Length)
                        Array.Resize(ref buffer, total + frame.HeaderSize);
                    if (available >= total)
                    {
                        lostSync = false;
                        var record = new FramedRecord(eventId, time, bufferOffset + start,
                            new ReadOnlyMemory<byte>(buffer, start + frame.HeaderSize, payloadLength));
                        start += total;
                        yield return record;
                        continue;
                    }
                    if (eof)
                    {
                        diagnostics.Report(bufferOffset + start, $"Record of event {eventId} runs past the end of the file ({available} of {total} bytes); it is skipped.");
                        if (frame.Sync.Length == 0)
                            yield break;
                        start++;
                        lostSync = true;
                        continue;
                    }
                }
                else
                {
                    if (frame.Sync.Length == 0)
                    {
                        diagnostics.Report(bufferOffset + start, "Record header is not valid and the format has no sync pattern to search for; the rest of the file is skipped.");
                        yield break;
                    }
                    if (!lostSync)
                    {
                        diagnostics.Report(bufferOffset + start, "Lost record sync; searching for the next record.");
                        lostSync = true;
                    }
                    var next = buffer.AsSpan(start + 1, available - 1).IndexOf(frame.Sync);
                    start = next >= 0 ? start + 1 + next : Math.Max(start + 1, end - frame.Sync.Length + 1);
                    continue;
                }
            }
            else if (eof)
            {
                if (available > 0 && !lostSync)
                    diagnostics.Report(bufferOffset + start, $"{available} bytes at the end of the file are not a whole record.");
                yield break;
            }

            if (start > 0)
            {
                Buffer.BlockCopy(buffer, start, buffer, 0, end - start);
                bufferOffset += start;
                end -= start;
                start = 0;
            }
            var read = stream.Read(buffer, end, buffer.Length - end);
            if (read == 0)
                eof = true;
            else
                end += read;
        }
    }
}

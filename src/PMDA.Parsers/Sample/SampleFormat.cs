using System.Buffers.Binary;
using System.Text;
using PMDA.Core.Events;
using PMDA.Core.Parsing;

namespace PMDA.Parsers.Sample;

/// <summary>
/// SYNTHETIC format used to develop and test the pipeline until the real customer ICDs are
/// available. It is not any customer's format. All values are little endian.
///
/// Header (48 bytes):
///   0  char[8]  magic "PMDASMP1"
///   8  u16      format version
///   10 u16      header length (48; larger values are skipped for forward compatibility)
///   12 u32      reserved
///   16 i64      flight start, UTC ticks
///   24 char[12] tail number, ASCII, zero padded
///   36 char[12] mission id, ASCII, zero padded
///
/// Record (14 + N + 2 bytes):
///   0  u16      sync 0xA55A
///   2  u16      message type
///   4  i64      time since flight start, ticks (100 ns)
///   12 u16      payload length N
///   14 u8[N]    payload
///   14+N u16    checksum: sum of payload bytes mod 65536
/// </summary>
public static class SampleFormat
{
    public const string CustomerId = "SAMPLE";
    public static ReadOnlySpan<byte> Magic => "PMDASMP1"u8;
    public const int HeaderSize = 48;
    public const ushort Sync = 0xA55A;
    public const int RecordHeaderSize = 14;
    public const int ChecksumSize = 2;

    public const ushort EmitterMessage = 0x0001;
    public const ushort CountermeasureMessage = 0x0002;
    public const ushort NavigationMessage = 0x0003;

    public static ushort Checksum(ReadOnlySpan<byte> payload)
    {
        uint sum = 0;
        foreach (var b in payload)
            sum += b;
        return (ushort)sum;
    }

    public static DecoderRegistry AddSampleDecoders(this DecoderRegistry registry) => registry
        .AddShared(new SampleEmitterDecoder())
        .AddShared(new SampleCountermeasureDecoder())
        .AddShared(new SampleNavigationDecoder());
}

public sealed class SampleAdapter : ICustomerAdapter
{
    private const int BufferSize = 1 << 20;
    private static ReadOnlySpan<byte> SyncBytes => [0x5A, 0xA5];

    public string CustomerId => SampleFormat.CustomerId;
    public int DetectionLength => SampleFormat.Magic.Length;

    public bool CanRead(ReadOnlySpan<byte> fileStart) => fileStart.StartsWith(SampleFormat.Magic);

    public FlightHeader ReadHeader(Stream stream)
    {
        Span<byte> h = stackalloc byte[SampleFormat.HeaderSize];
        stream.ReadExactly(h);
        if (!h.StartsWith(SampleFormat.Magic))
            throw new InvalidDataException("Not a sample-format file (bad magic).");

        var version = BinaryPrimitives.ReadUInt16LittleEndian(h[8..]);
        var headerLength = BinaryPrimitives.ReadUInt16LittleEndian(h[10..]);
        if (headerLength < SampleFormat.HeaderSize)
            throw new InvalidDataException($"Header length {headerLength} is smaller than {SampleFormat.HeaderSize}.");
        var startTicks = BinaryPrimitives.ReadInt64LittleEndian(h[16..]);
        if (startTicks < DateTime.MinValue.Ticks || startTicks > DateTime.MaxValue.Ticks)
            throw new InvalidDataException($"Flight start {startTicks} is not a valid time.");

        var skip = headerLength - SampleFormat.HeaderSize;
        if (skip > 0)
            stream.ReadExactly(new byte[skip]);

        return new FlightHeader(
            CustomerId,
            version,
            new DateTime(startTicks, DateTimeKind.Utc),
            ReadAscii(h.Slice(24, 12)),
            ReadAscii(h.Slice(36, 12)));
    }

    public IEnumerable<RawRecord> ReadRecords(Stream stream, FlightHeader header, IParseDiagnostics diagnostics, CancellationToken ct)
    {
        var buffer = new byte[BufferSize];
        int start = 0, end = 0;
        long bufferOffset = stream.Position; // file offset of buffer[0]
        var eof = false;
        var lostSync = false; // reports a run of garbage once, not once per byte

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var status = TryFrame(buffer.AsSpan(start, end - start), out var frame);
            switch (status)
            {
                case FrameStatus.Ok:
                    lostSync = false;
                    var record = new RawRecord(
                        frame.MessageType,
                        bufferOffset + start,
                        header.StartUtc.AddTicks(frame.TimeTicks),
                        new ReadOnlyMemory<byte>(buffer, start + SampleFormat.RecordHeaderSize, frame.PayloadLength));
                    start += frame.TotalLength;
                    yield return record;
                    break;

                case FrameStatus.BadChecksum:
                    diagnostics.Report(bufferOffset + start, $"Checksum mismatch in message 0x{frame.MessageType:X4}; record skipped.");
                    start += 1;
                    lostSync = true;
                    break;

                case FrameStatus.NoSync:
                    if (!lostSync)
                    {
                        diagnostics.Report(bufferOffset + start, "Lost record sync; scanning for next record.");
                        lostSync = true;
                    }
                    var next = buffer.AsSpan(start + 1, end - start - 1).IndexOf(SyncBytes);
                    // Keep the last byte when no sync was found: it may be the first half of one.
                    start = next >= 0 ? start + 1 + next : end - 1;
                    break;

                case FrameStatus.NeedMore when eof:
                    var remaining = end - start;
                    if (remaining >= SampleFormat.RecordHeaderSize)
                    {
                        // A record header whose length runs past the end of the file: likely a
                        // corrupt length field, so keep scanning instead of dropping the rest.
                        diagnostics.Report(bufferOffset + start, "Record length runs past end of file; record skipped.");
                        start += 1;
                        lostSync = true;
                        break;
                    }
                    if (remaining > 0 && !lostSync)
                        diagnostics.Report(bufferOffset + start, $"Truncated record at end of file ({remaining} bytes).");
                    yield break;

                case FrameStatus.NeedMore:
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
                    break;
            }
        }
    }

    private enum FrameStatus { Ok, NeedMore, NoSync, BadChecksum }

    private readonly record struct Frame(ushort MessageType, long TimeTicks, int PayloadLength)
    {
        public int TotalLength => SampleFormat.RecordHeaderSize + PayloadLength + SampleFormat.ChecksumSize;
    }

    private static FrameStatus TryFrame(ReadOnlySpan<byte> s, out Frame frame)
    {
        frame = default;
        if (s.Length >= 2 && BinaryPrimitives.ReadUInt16LittleEndian(s) != SampleFormat.Sync)
            return FrameStatus.NoSync;
        if (s.Length < SampleFormat.RecordHeaderSize)
            return FrameStatus.NeedMore;

        frame = new Frame(
            BinaryPrimitives.ReadUInt16LittleEndian(s[2..]),
            BinaryPrimitives.ReadInt64LittleEndian(s[4..]),
            BinaryPrimitives.ReadUInt16LittleEndian(s[12..]));
        if (s.Length < frame.TotalLength)
            return FrameStatus.NeedMore;

        var payload = s.Slice(SampleFormat.RecordHeaderSize, frame.PayloadLength);
        var checksum = BinaryPrimitives.ReadUInt16LittleEndian(s[(SampleFormat.RecordHeaderSize + frame.PayloadLength)..]);
        return checksum == SampleFormat.Checksum(payload) ? FrameStatus.Ok : FrameStatus.BadChecksum;
    }

    private static string ReadAscii(ReadOnlySpan<byte> field)
    {
        var zero = field.IndexOf((byte)0);
        return Encoding.ASCII.GetString(zero >= 0 ? field[..zero] : field).Trim();
    }
}

/// <summary>Payload: u32 emitter id, f32 freq MHz, f32 PRI us, f32 PW us, f32 bearing deg, f32 amplitude dB.</summary>
public sealed class SampleEmitterDecoder : IPayloadDecoder
{
    public const int PayloadSize = 24;
    public ushort MessageType => SampleFormat.EmitterMessage;

    public bool TryDecode(in RawRecord record, out FlightEvent evt, out string? error)
    {
        var p = record.Payload.Span;
        if (p.Length != PayloadSize)
        {
            evt = default;
            error = $"Emitter payload is {p.Length} bytes, expected {PayloadSize}.";
            return false;
        }
        evt = new FlightEvent
        {
            TimeUtc = record.TimeUtc,
            EventType = EventType.Emitter,
            EmitterId = (int)BinaryPrimitives.ReadUInt32LittleEndian(p),
            FrequencyMHz = BinaryPrimitives.ReadSingleLittleEndian(p[4..]),
            PriUs = BinaryPrimitives.ReadSingleLittleEndian(p[8..]),
            PulseWidthUs = BinaryPrimitives.ReadSingleLittleEndian(p[12..]),
            BearingDeg = BinaryPrimitives.ReadSingleLittleEndian(p[16..]),
            AmplitudeDb = BinaryPrimitives.ReadSingleLittleEndian(p[20..]),
        };
        error = null;
        return true;
    }
}

/// <summary>Payload: u8 kind (1 = chaff, 2 = flare), u8 count.</summary>
public sealed class SampleCountermeasureDecoder : IPayloadDecoder
{
    public const int PayloadSize = 2;
    public ushort MessageType => SampleFormat.CountermeasureMessage;

    public bool TryDecode(in RawRecord record, out FlightEvent evt, out string? error)
    {
        var p = record.Payload.Span;
        if (p.Length != PayloadSize)
        {
            evt = default;
            error = $"Countermeasure payload is {p.Length} bytes, expected {PayloadSize}.";
            return false;
        }
        evt = new FlightEvent
        {
            TimeUtc = record.TimeUtc,
            EventType = EventType.Countermeasure,
            SubType = p[0],
            Count = p[1],
        };
        error = null;
        return true;
    }
}

/// <summary>Payload: f64 lat deg, f64 lon deg, f32 alt m.</summary>
public sealed class SampleNavigationDecoder : IPayloadDecoder
{
    public const int PayloadSize = 20;
    public ushort MessageType => SampleFormat.NavigationMessage;

    public bool TryDecode(in RawRecord record, out FlightEvent evt, out string? error)
    {
        var p = record.Payload.Span;
        if (p.Length != PayloadSize)
        {
            evt = default;
            error = $"Navigation payload is {p.Length} bytes, expected {PayloadSize}.";
            return false;
        }
        evt = new FlightEvent
        {
            TimeUtc = record.TimeUtc,
            EventType = EventType.Navigation,
            Lat = BinaryPrimitives.ReadDoubleLittleEndian(p),
            Lon = BinaryPrimitives.ReadDoubleLittleEndian(p[8..]),
            AltM = BinaryPrimitives.ReadSingleLittleEndian(p[16..]),
        };
        error = null;
        return true;
    }
}

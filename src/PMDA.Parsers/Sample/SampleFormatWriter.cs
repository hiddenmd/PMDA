using System.Buffers.Binary;
using System.Text;

namespace PMDA.Parsers.Sample;

/// <summary>Writes synthetic sample-format files, for tests and benchmark data.</summary>
public sealed class SampleFormatWriter(Stream stream)
{
    private DateTime _start;

    public void WriteHeader(DateTime startUtc, string tail, string missionId, ushort version = 1)
    {
        _start = startUtc;
        Span<byte> h = stackalloc byte[SampleFormat.HeaderSize];
        h.Clear();
        SampleFormat.Magic.CopyTo(h);
        BinaryPrimitives.WriteUInt16LittleEndian(h[8..], version);
        BinaryPrimitives.WriteUInt16LittleEndian(h[10..], SampleFormat.HeaderSize);
        BinaryPrimitives.WriteInt64LittleEndian(h[16..], startUtc.Ticks);
        Encoding.ASCII.GetBytes(tail.AsSpan(0, Math.Min(tail.Length, 12)), h.Slice(24, 12));
        Encoding.ASCII.GetBytes(missionId.AsSpan(0, Math.Min(missionId.Length, 12)), h.Slice(36, 12));
        stream.Write(h);
    }

    public void WriteEmitter(TimeSpan offset, uint emitterId, float freqMHz, float priUs, float pwUs, float bearingDeg, float amplitudeDb)
    {
        Span<byte> p = stackalloc byte[SampleEmitterDecoder.PayloadSize];
        BinaryPrimitives.WriteUInt32LittleEndian(p, emitterId);
        BinaryPrimitives.WriteSingleLittleEndian(p[4..], freqMHz);
        BinaryPrimitives.WriteSingleLittleEndian(p[8..], priUs);
        BinaryPrimitives.WriteSingleLittleEndian(p[12..], pwUs);
        BinaryPrimitives.WriteSingleLittleEndian(p[16..], bearingDeg);
        BinaryPrimitives.WriteSingleLittleEndian(p[20..], amplitudeDb);
        WriteRecord(SampleFormat.EmitterMessage, offset, p);
    }

    public void WriteCountermeasure(TimeSpan offset, byte kind, byte count) =>
        WriteRecord(SampleFormat.CountermeasureMessage, offset, [kind, count]);

    public void WriteNavigation(TimeSpan offset, double lat, double lon, float altM)
    {
        Span<byte> p = stackalloc byte[SampleNavigationDecoder.PayloadSize];
        BinaryPrimitives.WriteDoubleLittleEndian(p, lat);
        BinaryPrimitives.WriteDoubleLittleEndian(p[8..], lon);
        BinaryPrimitives.WriteSingleLittleEndian(p[16..], altM);
        WriteRecord(SampleFormat.NavigationMessage, offset, p);
    }

    public void WriteRecord(ushort messageType, TimeSpan offset, ReadOnlySpan<byte> payload, bool corruptChecksum = false)
    {
        Span<byte> h = stackalloc byte[SampleFormat.RecordHeaderSize];
        BinaryPrimitives.WriteUInt16LittleEndian(h, SampleFormat.Sync);
        BinaryPrimitives.WriteUInt16LittleEndian(h[2..], messageType);
        BinaryPrimitives.WriteInt64LittleEndian(h[4..], offset.Ticks);
        BinaryPrimitives.WriteUInt16LittleEndian(h[12..], checked((ushort)payload.Length));
        stream.Write(h);
        stream.Write(payload);

        Span<byte> c = stackalloc byte[SampleFormat.ChecksumSize];
        var checksum = SampleFormat.Checksum(payload);
        BinaryPrimitives.WriteUInt16LittleEndian(c, corruptChecksum ? (ushort)~checksum : checksum);
        stream.Write(c);
    }

    public void WriteGarbage(ReadOnlySpan<byte> bytes) => stream.Write(bytes);

    public DateTime StartUtc => _start;
}

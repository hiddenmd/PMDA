using PMDA.Core.Events;

namespace PMDA.Core.Parsing;

public sealed record FlightHeader(
    string CustomerId,
    int FormatVersion,
    DateTime StartUtc,
    string Tail,
    string MissionId);

/// <summary>
/// One framed record from a flight file, before payload decoding.
/// <see cref="Payload"/> may point into a reused read buffer: it is only valid until the
/// enumerator advances. Decoders must copy anything they keep.
/// </summary>
public readonly record struct RawRecord(
    ushort MessageType,
    long FileOffset,
    DateTime TimeUtc,
    ReadOnlyMemory<byte> Payload);

/// <summary>Receives problems found while parsing; parsing continues after a report.</summary>
public interface IParseDiagnostics
{
    void Report(long fileOffset, string message);
}

/// <summary>
/// Knows one customer's file header and record framing. Payload content is left to decoders.
/// </summary>
public interface ICustomerAdapter
{
    string CustomerId { get; }

    /// <summary>Bytes needed by <see cref="CanRead"/> to recognize the format.</summary>
    int DetectionLength { get; }

    bool CanRead(ReadOnlySpan<byte> fileStart);

    /// <summary>Reads the header and leaves the stream positioned at the first record.</summary>
    FlightHeader ReadHeader(Stream stream);

    IEnumerable<RawRecord> ReadRecords(Stream stream, FlightHeader header, IParseDiagnostics diagnostics, CancellationToken ct);
}

/// <summary>Decodes the payload of one message type into a <see cref="FlightEvent"/>.</summary>
public interface IPayloadDecoder
{
    ushort MessageType { get; }

    /// <summary>Returns false when the payload is malformed; <paramref name="error"/> says why.</summary>
    bool TryDecode(in RawRecord record, out FlightEvent evt, out string? error);
}

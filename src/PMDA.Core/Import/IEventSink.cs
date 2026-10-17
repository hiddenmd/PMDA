using PMDA.Core.Events;
using PMDA.Core.Parsing;

namespace PMDA.Core.Import;

public sealed record FlightImportInfo(
    Guid FlightId,
    FlightHeader Header,
    string SourceName,
    byte[]? FileSha256);

public sealed record FlightImportSummary(
    Guid FlightId,
    long EventCount,
    long ErrorCount,
    DateTime? FirstEventUtc,
    DateTime? LastEventUtc);

public enum BeginFlightResult
{
    Started,
    /// <summary>A flight with the same file hash was already imported.</summary>
    Duplicate,
}

/// <summary>Destination for decoded events (SQL Server, in-memory for tests, ...).</summary>
public interface IEventSink
{
    Task<BeginFlightResult> BeginFlightAsync(FlightImportInfo info, CancellationToken ct);

    /// <summary>The sink must not keep a reference to <paramref name="events"/> after returning.</summary>
    Task WriteBatchAsync(Guid flightId, ReadOnlyMemory<FlightEvent> events, CancellationToken ct);

    Task CompleteFlightAsync(FlightImportSummary summary, CancellationToken ct);

    /// <summary>Removes everything written for the flight and marks it failed.</summary>
    Task AbortFlightAsync(Guid flightId, string reason, CancellationToken ct);
}

using PMDA.Formats.Projects;

namespace PMDA.Formats.Recipes;

/// <param name="Changes">Tables created and columns added.</param>
/// <param name="Problems">What could not be changed, e.g. a column whose type differs from the header's.</param>
public sealed record PrepareResult(IReadOnlyList<string> Changes, IReadOnlyList<string> Problems);

/// <summary>Destination for a header-defined flight's typed rows and raw records (SQL Server, memory for tests).</summary>
public interface IFormatRowSink
{
    /// <summary>Creates or extends the project's tables so they hold every column of <paramref name="format"/>.</summary>
    Task<PrepareResult> PrepareAsync(CompiledFormat format, int version, string headerText, CancellationToken ct);

    /// <summary>Buffers a row; may write a batch before returning.</summary>
    ValueTask AddAsync(Guid flightId, OutputRow row, CancellationToken ct);

    /// <summary>Buffers a raw record (copied); may write a batch before returning.</summary>
    ValueTask AddRawAsync(Guid flightId, long seq, DateTime timeUtc, int eventId, ReadOnlyMemory<byte> payload, CancellationToken ct);

    Task FlushAsync(CancellationToken ct);

    /// <summary>Removes everything written for the flight.</summary>
    Task DeleteFlightAsync(Guid flightId, CancellationToken ct);
}

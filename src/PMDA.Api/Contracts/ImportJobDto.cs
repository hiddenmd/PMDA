using PMDA.Api.Imports;

namespace PMDA.Api.Contracts;

/// <param name="Version">Increases with every change; clients drop updates older than what they have.</param>
public sealed record ImportJobDto(
    Guid Id,
    long Version,
    string FileName,
    long SizeBytes,
    ImportState State,
    DateTime CreatedUtc,
    DateTime? StartedUtc,
    DateTime? FinishedUtc,
    long BytesRead,
    long TotalBytes,
    long EventCount,
    long ErrorCount,
    Guid? FlightId,
    string? FailureReason)
{
    public static ImportJobDto From(ImportJobSnapshot s) => new(
        s.Id,
        s.Version,
        s.FileName,
        s.SizeBytes,
        s.State,
        s.CreatedUtc,
        s.StartedUtc,
        s.FinishedUtc,
        Math.Max(0, s.Progress?.BytesRead ?? 0),
        s.Progress is { TotalBytes: > 0 } p ? p.TotalBytes : s.SizeBytes,
        s.Progress?.EventCount ?? 0,
        s.Progress?.ErrorCount ?? 0,
        s.FlightId,
        s.FailureReason);
}

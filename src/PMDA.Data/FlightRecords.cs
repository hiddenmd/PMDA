namespace PMDA.Data;

public enum FlightStatus : byte
{
    Importing = 0,
    Completed = 1,
    Failed = 2,
}

public sealed record FlightRecord(
    Guid FlightId,
    string CustomerId,
    int FormatVersion,
    string Tail,
    string MissionId,
    DateTime StartUtc,
    DateTime? FirstEventUtc,
    DateTime? LastEventUtc,
    string SourceName,
    FlightStatus Status,
    string? FailureReason,
    long EventCount,
    long ErrorCount,
    DateTime ImportedUtc);

public sealed record DiagnosticRecord(long FileOffset, string Message);

public sealed record FlightQuery(int Page = 1, int PageSize = 50, string? CustomerId = null, string? Tail = null);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long Total);

namespace PMDA.Data;

public enum FlightStatus : byte
{
    Importing = 0,
    Completed = 1,
    Failed = 2,
    /// <summary>Deleted by a user: hidden everywhere, events being purged in the background.</summary>
    Deleting = 3,
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

public enum FlightSort
{
    ImportedNewest,
    StartNewest,
    StartOldest,
    Tail,
    MostEvents,
}

/// <summary>Flight list filters. Text filters match any part of the value; null means "any".</summary>
public sealed record FlightQuery(
    int Page = 1,
    int PageSize = 50,
    string? CustomerId = null,
    string? Tail = null,
    string? Mission = null,
    FlightStatus? Status = null,
    DateTime? StartFrom = null,
    DateTime? StartTo = null,
    bool? HasProblems = null,
    FlightSort Sort = FlightSort.ImportedNewest);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long Total);

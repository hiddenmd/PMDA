using PMDA.Api.Database;
using PMDA.Data;

namespace PMDA.Api.Contracts;

public sealed record FlightSummaryDto(
    Guid Id,
    string CustomerId,
    string Tail,
    string MissionId,
    DateTime StartUtc,
    FlightStatus Status,
    long EventCount,
    long ErrorCount,
    DateTime ImportedUtc,
    string DatabaseId,
    string DatabaseName)
{
    public static FlightSummaryDto From(FlightRecord f, FlightStore store) =>
        new(f.FlightId, f.CustomerId, f.Tail, f.MissionId, f.StartUtc, f.Status, f.EventCount, f.ErrorCount, f.ImportedUtc, store.Id, store.Name);
}

public sealed record FlightDetailDto(
    Guid Id,
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
    DateTime ImportedUtc,
    string DatabaseId,
    string DatabaseName,
    bool DatabaseReadOnly)
{
    public static FlightDetailDto From(FlightRecord f, FlightStore store) => new(
        f.FlightId, f.CustomerId, f.FormatVersion, f.Tail, f.MissionId, f.StartUtc, f.FirstEventUtc, f.LastEventUtc,
        f.SourceName, f.Status, f.FailureReason, f.EventCount, f.ErrorCount, f.ImportedUtc, store.Id, store.Name, store.ReadOnly);
}

public sealed record DiagnosticDto(long FileOffset, string Message);

/// <summary>A database that could not be queried; the page holds the flights of the others.</summary>
public sealed record UnavailableDatabaseDto(string Id, string Name, string Message);

public sealed record FlightPageDto(IReadOnlyList<FlightSummaryDto> Items, int Page, int PageSize, long Total, IReadOnlyList<UnavailableDatabaseDto> Unavailable);

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
    DateTime ImportedUtc)
{
    public static FlightSummaryDto From(FlightRecord f) =>
        new(f.FlightId, f.CustomerId, f.Tail, f.MissionId, f.StartUtc, f.Status, f.EventCount, f.ErrorCount, f.ImportedUtc);
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
    DateTime ImportedUtc)
{
    public static FlightDetailDto From(FlightRecord f) => new(
        f.FlightId, f.CustomerId, f.FormatVersion, f.Tail, f.MissionId, f.StartUtc, f.FirstEventUtc, f.LastEventUtc,
        f.SourceName, f.Status, f.FailureReason, f.EventCount, f.ErrorCount, f.ImportedUtc);
}

public sealed record DiagnosticDto(long FileOffset, string Message);

public sealed record FlightPageDto(IReadOnlyList<FlightSummaryDto> Items, int Page, int PageSize, long Total);

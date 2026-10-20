using PMDA.Formats.Decoding;
using PMDA.Formats.Dialect;
using PMDA.Formats.Projects;
using PMDA.Formats.Recipes;

namespace PMDA.Api.Contracts;

public sealed record FormatSummaryDto(string Id, string Name, int? Version, bool CanImport, int Messages, int Errors, int Warnings);

public sealed record FormatDiagnosticDto(int Line, string Severity, string Message);

public sealed record FormatColumnDto(string Field, string Name, string Type, string? Title, string? Unit, string? Enum);

public sealed record FormatChildDto(string Field, string Table, string CountField, int MaxCount, IReadOnlyList<FormatColumnDto> Columns);

/// <param name="Events">Recorded event ids decoded with this struct.</param>
/// <param name="Stored">Whether rows of this message are written to a table (tracked events are not).</param>
public sealed record FormatMessageDto(
    string Key,
    string Table,
    IReadOnlyList<int> Events,
    bool Stored,
    string? Title,
    string? Group,
    int? Level,
    bool Hidden,
    int SizeBytes,
    IReadOnlyList<FormatColumnDto> Columns,
    IReadOnlyList<FormatChildDto> Children);

public sealed record FormatFrameDto(int HeaderBytes, string Sync, string Length, int LengthUnitBytes);

public sealed record FormatVersionDto(int Version, string Sha256, long Bytes, DateTime SavedUtc);

public sealed record FormatDetailDto(
    FormatProject Project,
    int? Version,
    IReadOnlyList<FormatVersionDto> Versions,
    bool CanImport,
    string SqlSchema,
    FormatFrameDto? Frame,
    IReadOnlyList<FormatDiagnosticDto> Diagnostics,
    IReadOnlyList<FormatMessageDto> Messages,
    int Enums,
    IReadOnlyList<string> EventProperties);

public sealed record FormatUploadResultDto(FormatDetailDto Detail, FormatDiff? Diff, bool NewVersion);

public sealed record CreateFormatRequest(string Id, string Name);

public sealed record DryRunDto(
    DateTime FlightDate,
    bool DateFound,
    string Tail,
    long Records,
    DateTime? FirstUtc,
    DateTime? LastUtc,
    IReadOnlyDictionary<string, long> RecordsByEvent,
    IReadOnlyDictionary<string, long> UnknownEvents,
    IReadOnlyDictionary<string, long> ShortByEvent,
    IReadOnlyDictionary<string, long> RowsByTable,
    long TracksStarted,
    long UpdatesWithoutNew,
    long ExtensionsWithoutNew,
    long DeletesWithoutNew,
    long UnknownJoinKinds,
    long Events,
    IReadOnlyList<ParseDiagnosticDto> Diagnostics,
    long DiagnosticCount)
{
    public static DryRunDto From(DryRunReport r)
    {
        static Dictionary<string, long> Keys(Dictionary<int, long> d) => d.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);
        var s = r.Stats;
        return new DryRunDto(r.Flight.Date, r.Flight.DateFound, r.Flight.Tail, r.Records, r.FirstUtc, r.LastUtc,
            Keys(s.RecordsByEvent), Keys(s.UnknownEvents), Keys(s.ShortByEvent), s.RowsByTable,
            s.TracksStarted, s.UpdatesWithoutNew, s.ExtensionsWithoutNew, s.DeletesWithoutNew, s.UnknownJoinKinds, s.Events,
            r.Diagnostics.Select(d => new ParseDiagnosticDto(d.FileOffset, d.Message)).ToList(), r.DiagnosticCount);
    }
}

public sealed record ParseDiagnosticDto(long FileOffset, string Message);

public static class FormatDtoMapping
{
    public static FormatDetailDto Detail(FormatProject project, int? version, IEnumerable<FormatVersionDto> versions, CompiledFormat? format)
    {
        if (format is null)
            return new FormatDetailDto(project, version, versions.ToList(), false, CompiledFormat.SchemaFromId(project.Id), null,
                [new FormatDiagnosticDto(0, nameof(Severity.Info), "No header yet: upload the vendor .h file.")], [], 0, RecipeEngine.EventProperties);

        var stored = format.StoredTables.Select(p => p.Key).ToHashSet();
        var eventsByKey = format.Decoders.GroupBy(d => d.Value.Plan.Key).ToDictionary(g => g.Key, g => g.Select(x => x.Key).Order().ToList());
        var messages = format.Tables.Values
            .OrderBy(p => eventsByKey.TryGetValue(p.Key, out var e) && e.Count > 0 ? e[0] : int.MaxValue)
            .Select(p => new FormatMessageDto(
                p.Key,
                p.TableName,
                eventsByKey.GetValueOrDefault(p.Key) ?? [],
                stored.Contains(p.Key),
                p.Decl.Tags.Value("Title"),
                p.Decl.Tags.Value("Group"),
                int.TryParse(p.Decl.Tags.Value("Level"), out var level) ? level : null,
                p.Decl.Tags.Has("InVisible"),
                format.Decoders.Values.FirstOrDefault(d => d.Plan.Key == p.Key && d.Plan.Decl.Id is not null)?.Layout.Size
                    ?? format.Decoders.Values.FirstOrDefault(d => d.Plan.Key == p.Key)?.Layout.Size ?? 0,
                p.Columns.Select(Column).ToList(),
                p.Children.Select(c => new FormatChildDto(c.Field, $"{p.TableName}_{c.Name}", c.CountField, c.MaxCount, c.Columns.Select(Column).ToList())).ToList()))
            .ToList();
        var frame = format.Frame is { } f
            ? new FormatFrameDto(f.HeaderSize, Convert.ToHexString(f.Sync), f.LengthIncludesHeader ? "All" : "DataOnly", f.LengthUnitBytes)
            : null;
        return new FormatDetailDto(project, version, versions.ToList(), format.CanImport, format.SqlSchema, frame,
            format.Diagnostics.Select(d => new FormatDiagnosticDto(d.Line, d.Severity.ToString(), d.Message)).ToList(),
            messages, format.Model.Enums.Count, RecipeEngine.EventProperties);
    }

    private static FormatColumnDto Column(ColumnDef c) => new(c.Field, c.Name, c.Type.ToString(), c.Title, c.Unit, c.Enum);
}

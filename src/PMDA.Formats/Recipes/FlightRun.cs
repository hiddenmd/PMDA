using System.Globalization;
using PMDA.Core.Events;
using PMDA.Core.Import;
using PMDA.Core.Parsing;
using PMDA.Formats.Framing;
using PMDA.Formats.Projects;

namespace PMDA.Formats.Recipes;

/// <summary>What a raw dump says about its flight before the events are read.</summary>
public sealed record FlightInfo(DateTime Date, string Tail, bool DateFound);

/// <summary>Turns the header's time-of-day ticks into UTC, moving to the next day when the clock wraps at midnight.</summary>
public sealed class FlightClock(DateTime date, double unitSeconds)
{
    private const double HalfDay = 43_200;
    private int _days;
    private double _last = -1;

    public DateTime ToUtc(long ticks)
    {
        var seconds = ticks * unitSeconds;
        if (_last >= 0 && seconds < _last - HalfDay)
            _days++;
        _last = seconds;
        // Whole 100 ns ticks, so 0.001 s units land exactly on milliseconds.
        return DateTime.SpecifyKind(date.Date.AddDays(_days).AddTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond)), DateTimeKind.Utc);
    }
}

public static class FlightRun
{
    /// <summary>
    /// Reads the first flight-info record (date, tail). The stream is rewound afterwards. Records before it carry no
    /// date, so the scan looks ahead at most <paramref name="maxRecords"/> records.
    /// </summary>
    public static FlightInfo ScanInfo(CompiledFormat format, Stream stream, int maxRecords = 200_000)
    {
        var fallback = format.Project.Settings.DefaultDate is { } d && DateTime.TryParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : new DateTime(2000, 1, 1);
        if (format.Frame is not { } frame || format.Project.Recipe.FlightInfo is not { } info || format.FindMessage(info.Message) is not { } plan)
            return new FlightInfo(fallback, "", false);

        var start = stream.Position;
        try
        {
            var n = 0;
            foreach (var record in RecordReader.Read(stream, frame, NoDiagnostics.Instance, CancellationToken.None))
            {
                if (++n > maxRecords)
                    break;
                if (!format.Decoders.TryGetValue(record.EventId, out var decoder) || decoder.Plan.Key != plan.Key)
                    continue;
                var decoded = decoder.Decode(record.Payload.Span);
                var tail = info.Tail is { } t ? Convert.ToString(decoded[t] ?? decoder.ReadRaw(record.Payload.Span, t), CultureInfo.InvariantCulture) ?? "" : "";
                long? Part(string? field) => field is null ? null : decoder.ReadRaw(record.Payload.Span, field);
                var (year, month, day) = (Part(info.Year), Part(info.Month), Part(info.Day));
                if (year is { } y && month is { } m && day is { } dd && m is >= 1 and <= 12 && dd is >= 1 and <= 31)
                {
                    var fullYear = (int)y < 100 ? info.YearBase + (int)y : (int)y;
                    if (fullYear is >= 1900 and <= 2200 && dd <= DateTime.DaysInMonth(fullYear, (int)m))
                        return new FlightInfo(new DateTime(fullYear, (int)m, (int)dd), tail.Trim(), true);
                }
                return new FlightInfo(fallback, tail.Trim(), false);
            }
            return new FlightInfo(fallback, "", false);
        }
        finally
        {
            stream.Position = start;
        }
    }

    /// <summary>Decodes a whole file without storing anything: what an import would produce.</summary>
    public static DryRunReport DryRun(CompiledFormat format, Stream stream, CancellationToken ct = default)
    {
        var diagnostics = new CollectingDiagnostics(200);
        if (format.Frame is not { } frame)
            return new DryRunReport(new FlightInfo(new DateTime(2000, 1, 1), "", false), 0, null, null, new RecipeStats(), [new ParseDiagnostic(0, "The format cannot frame records (see the header report).")], 1);

        var info = ScanInfo(format, stream);
        var clock = new FlightClock(info.Date, format.Project.Settings.TimeUnitSeconds);
        var engine = new RecipeEngine(format);
        long seq = 0;
        DateTime? first = null, last = null;
        foreach (var record in RecordReader.Read(stream, frame, diagnostics, ct))
        {
            var time = clock.ToUtc(record.Time);
            engine.Process(record, time, seq++, static _ => { }, static _ => { });
            first ??= time;
            last = time;
        }
        return new DryRunReport(info, seq, first, last, engine.Stats, diagnostics.Items, diagnostics.Count);
    }

    private sealed class NoDiagnostics : IParseDiagnostics
    {
        public static readonly NoDiagnostics Instance = new();
        public void Report(long fileOffset, string message) { }
    }
}

public sealed record DryRunReport(
    FlightInfo Flight,
    long Records,
    DateTime? FirstUtc,
    DateTime? LastUtc,
    RecipeStats Stats,
    IReadOnlyList<ParseDiagnostic> Diagnostics,
    long DiagnosticCount);

/// <summary>Keeps the first diagnostics and counts all of them.</summary>
public sealed class CollectingDiagnostics(int max) : IParseDiagnostics
{
    private readonly List<ParseDiagnostic> _items = new();
    public long Count { get; private set; }
    public IReadOnlyList<ParseDiagnostic> Items => _items;

    public void Report(long fileOffset, string message)
    {
        Count++;
        if (_items.Count < max)
            _items.Add(new ParseDiagnostic(fileOffset, message));
    }
}

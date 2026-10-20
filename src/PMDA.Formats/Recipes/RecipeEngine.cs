using System.Globalization;
using PMDA.Core.Events;
using PMDA.Formats.Decoding;
using PMDA.Formats.Framing;
using PMDA.Formats.Projects;

namespace PMDA.Formats.Recipes;

/// <summary>One row for a generated table.</summary>
public sealed record OutputRow(MessagePlan Plan, object?[] Values, List<object?[]>?[] Children, long RecordSeq, DateTime TimeUtc, int EventId);

public enum RecordOutcome { Stored, Tracked, Unknown }

/// <summary>Counts for a dry run or an import report.</summary>
public sealed class RecipeStats
{
    public Dictionary<int, long> RecordsByEvent { get; } = new();
    public Dictionary<int, long> ShortByEvent { get; } = new();
    public Dictionary<int, long> UnknownEvents { get; } = new();
    public Dictionary<string, long> RowsByTable { get; } = new(StringComparer.Ordinal);
    public long TracksStarted { get; set; }
    public long UpdatesWithoutNew { get; set; }
    public long ExtensionsWithoutNew { get; set; }
    public long DeletesWithoutNew { get; set; }
    public long UnknownJoinKinds { get; set; }
    public long Events { get; set; }

    internal static void Bump<TKey>(Dictionary<TKey, long> d, TKey key) where TKey : notnull =>
        d[key] = d.TryGetValue(key, out var n) ? n + 1 : 1;
}

/// <summary>
/// Runs a project's recipe over decoded records: plain messages become table rows, tracked objects become rows of
/// their derived table with lifecycle status, joined lists and aircraft state, and event rules feed the common
/// event model. Records must arrive in file order.
/// </summary>
public sealed class RecipeEngine
{
    private readonly CompiledFormat _format;
    private readonly Dictionary<int, TrackRun> _trackByEvent = new();
    private readonly List<TrackRun> _tracks = new();
    private readonly Dictionary<string, List<BoundRule>> _rules = new(StringComparer.Ordinal);
    private readonly NavRun? _nav;
    private readonly Dictionary<string, double?> _lastNav = new(StringComparer.OrdinalIgnoreCase);

    public RecipeEngine(CompiledFormat format)
    {
        _format = format;
        foreach (var t in format.Project.Recipe.Tracks)
        {
            if (format.Model.FindStruct(t.Struct) is not { } decl || !format.Tables.TryGetValue(decl.DisplayName, out var plan))
                continue;
            var run = new TrackRun(t, plan);
            _tracks.Add(run);
            foreach (var id in t.New) _trackByEvent[id] = run;
            foreach (var id in t.Update) _trackByEvent[id] = run;
            foreach (var id in t.Delete) _trackByEvent[id] = run;
            foreach (var id in t.Extension) _trackByEvent[id] = run;
        }

        if (format.Project.Recipe.Nav is { } nav && format.FindMessage(nav.Message) is { } navPlan)
            _nav = new NavRun(navPlan, nav.Fields.Select(f => (f.Key, FieldExpr.Parse(f.Value).Bind(navPlan))).Where(f => f.Item2 is not null).Select(f => (f.Key, f.Item2!.Value)).ToList());

        foreach (var rule in format.Project.Events)
        {
            if (format.FindMessage(rule.Message) is not { } plan)
                continue;
            var fields = new List<(string Property, FieldExpr.Bound Expr)>();
            foreach (var (property, expr) in rule.Fields)
                if (FieldExpr.Parse(expr).Bind(plan) is { } bound)
                    fields.Add((property, bound));
            Func<object?[], bool>? when = null;
            if (rule.When is { Length: > 0 } w)
                when = Condition.BindAll(w, plan) ?? (static _ => false); // a filter on a missing field matches nothing
            if (!_rules.TryGetValue(plan.Key, out var list))
                _rules[plan.Key] = list = new();
            list.Add(new BoundRule((EventType)(byte)rule.Kind, when, fields));
        }
    }

    public RecipeStats Stats { get; } = new();

    /// <summary>Handles one framed record; rows and events go to the callbacks in file order.</summary>
    public RecordOutcome Process(in FramedRecord record, DateTime timeUtc, long seq, Action<OutputRow> rows, Action<FlightEvent> events)
    {
        if (!_format.Decoders.TryGetValue(record.EventId, out var decoder))
        {
            RecipeStats.Bump(Stats.UnknownEvents, record.EventId);
            return RecordOutcome.Unknown;
        }
        RecipeStats.Bump(Stats.RecordsByEvent, record.EventId);
        var payload = record.Payload.Span;
        var decoded = decoder.Decode(payload);
        if (decoded.Short)
            RecipeStats.Bump(Stats.ShortByEvent, record.EventId);

        if (_nav is not null && decoded.Plan.Key == _nav.Plan.Key)
            foreach (var (role, expr) in _nav.Fields)
                _lastNav[role] = expr.Evaluate(decoded.Values);

        if (_trackByEvent.TryGetValue(record.EventId, out var track))
        {
            if (Track(track, record.EventId, decoder, payload, decoded) is { } values)
                Emit(new OutputRow(track.Plan, values.Values, values.Children, seq, timeUtc, record.EventId), rows, events);
            return RecordOutcome.Tracked;
        }
        Emit(new OutputRow(decoded.Plan, decoded.Values, decoded.Children, seq, timeUtc, record.EventId), rows, events);
        return RecordOutcome.Stored;
    }

    private void Emit(OutputRow row, Action<OutputRow> rows, Action<FlightEvent> events)
    {
        RecipeStats.Bump(Stats.RowsByTable, row.Plan.TableName);
        rows(row);
        if (!_rules.TryGetValue(row.Plan.Key, out var rules))
            return;
        foreach (var rule in rules)
        {
            if (rule.When is { } when && !when(row.Values))
                continue;
            var evt = new FlightEvent { TimeUtc = row.TimeUtc, EventType = rule.Type };
            foreach (var (property, expr) in rule.Fields)
                evt = Set(evt, property, expr.Evaluate(row.Values));
            Stats.Events++;
            events(evt);
        }
    }

    private static FlightEvent Set(FlightEvent e, string property, double? v) => property switch
    {
        nameof(FlightEvent.EmitterId) => e with { EmitterId = v is { } x ? (int)x : null },
        nameof(FlightEvent.SubType) => e with { SubType = v is { } x ? (short)x : null },
        nameof(FlightEvent.FrequencyMHz) => e with { FrequencyMHz = (float?)v },
        nameof(FlightEvent.PriUs) => e with { PriUs = (float?)v },
        nameof(FlightEvent.PulseWidthUs) => e with { PulseWidthUs = (float?)v },
        nameof(FlightEvent.BearingDeg) => e with { BearingDeg = (float?)v },
        nameof(FlightEvent.AmplitudeDb) => e with { AmplitudeDb = (float?)v },
        nameof(FlightEvent.Lat) => e with { Lat = v },
        nameof(FlightEvent.Lon) => e with { Lon = v },
        nameof(FlightEvent.AltM) => e with { AltM = (float?)v },
        nameof(FlightEvent.Count) => e with { Count = v is { } x ? (int)x : null },
        _ => e,
    };

    /// <summary>The property names an event rule may set.</summary>
    public static readonly string[] EventProperties =
    [
        nameof(FlightEvent.EmitterId), nameof(FlightEvent.SubType), nameof(FlightEvent.FrequencyMHz), nameof(FlightEvent.PriUs),
        nameof(FlightEvent.PulseWidthUs), nameof(FlightEvent.BearingDeg), nameof(FlightEvent.AmplitudeDb),
        nameof(FlightEvent.Lat), nameof(FlightEvent.Lon), nameof(FlightEvent.AltM), nameof(FlightEvent.Count),
    ];

    private (object?[] Values, List<object?[]>?[] Children)? Track(TrackRun run, int eventId, RecordDecoder decoder, ReadOnlySpan<byte> payload, DecodedRecord decoded)
    {
        var t = run.Recipe;
        if (decoder.ReadRaw(payload, t.Key) is not { } key)
            return null;
        var exists = run.Live.TryGetValue(key, out var state);
        if (!exists)
        {
            state = new TrackState(new object?[run.Plan.Columns.Count], new List<object?[]>?[run.Plan.Children.Count]);
            run.Live[key] = state;
        }

        int? status;
        if (t.New.Contains(eventId))
        {
            if (exists)
            {
                Array.Clear(state!.Values);
                Array.Clear(state.Children);
            }
            Stats.TracksStarted++;
            Merge(run, state!, decoded);
            status = t.Status?.New;
        }
        else if (t.Update.Contains(eventId))
        {
            if (!exists) Stats.UpdatesWithoutNew++;
            Merge(run, state!, decoded);
            status = t.Status?.Update;
        }
        else if (t.Extension.Contains(eventId))
        {
            if (!exists) Stats.ExtensionsWithoutNew++;
            Join(run, state!, decoder, payload);
            status = t.Status?.Extension;
        }
        else
        {
            if (!exists) Stats.DeletesWithoutNew++;
            if (t.DeleteCodeFrom is { } from && t.DeleteCodeTo is { } to && run.Plan.IndexOf(to) is var di and >= 0)
                state!.Values[di] = decoder.ReadRaw(payload, from) is { } dropCode ? ColumnValue(run.Plan.Columns[di], dropCode) : null;
            status = t.Status?.Delete;
            run.Live.Remove(key);
        }

        // The key column, even when the event's own struct is different from the track's.
        if (run.Plan.IndexOf(t.Key) is var ki and >= 0)
            state!.Values[ki] ??= ColumnValue(run.Plan.Columns[ki], key);
        if (t.Status is { } s && status is { } code && run.Plan.IndexOf(s.Field) is var si and >= 0)
            state!.Values[si] = ColumnValue(run.Plan.Columns[si], code);
        foreach (var (field, role) in t.AttachNav)
            if (run.Plan.IndexOf(field) is var ni and >= 0)
                state!.Values[ni] = _lastNav.GetValueOrDefault(role);

        return ((object?[])state!.Values.Clone(), (List<object?[]>?[])state.Children.Clone());
    }

    private static object ColumnValue(ColumnDef column, long value) => column.Type switch
    {
        ColumnType.BigInt => value,
        ColumnType.Float => (double)value,
        ColumnType.Text => value.ToString(CultureInfo.InvariantCulture),
        _ => (int)value,
    };

    /// <summary>Copies the event's decoded fields over the track's state; fields the event lacks keep their value.</summary>
    private static void Merge(TrackRun run, TrackState state, DecodedRecord decoded)
    {
        var same = ReferenceEquals(decoded.Plan.Decl, run.Plan.Decl);
        for (var i = 0; i < decoded.Values.Length; i++)
        {
            if (decoded.Values[i] is not { } v)
                continue;
            var target = same ? i : run.Plan.IndexOf(decoded.Plan.Columns[i].Field);
            if (target >= 0)
                state.Values[target] = v;
        }
        for (var i = 0; i < decoded.Children.Length; i++)
        {
            if (decoded.Children[i] is not { } rows)
                continue;
            var target = same ? i : run.Plan.Children.ToList().FindIndex(c => c.Field == decoded.Plan.Children[i].Field);
            if (target >= 0)
                state.Children[target] = rows;
        }
    }

    private void Join(TrackRun run, TrackState state, RecordDecoder decoder, ReadOnlySpan<byte> payload)
    {
        if (run.Recipe.Join is not { } join)
            return;
        var kind = decoder.ReadRaw(payload, join.Kind);
        if (kind is null || !join.Lists.TryGetValue(kind.Value.ToString(CultureInfo.InvariantCulture), out var target))
        {
            Stats.UnknownJoinKinds++;
            return;
        }
        var childIndex = run.Plan.Children.ToList().FindIndex(c => c.Field == target.Target);
        var values = decoder.Layout.Members.FirstOrDefault(m => m.Field.Name == join.Values);
        if (childIndex < 0 || values is null)
            return;
        var child = run.Plan.Children[childIndex];
        var count = decoder.ReadRaw(payload, join.Count) ?? 0;
        var bytes = join.CountUnit.Equals("elements", StringComparison.OrdinalIgnoreCase) ? count * child.Stride : count * 4;
        bytes = Math.Clamp(bytes, 0, Math.Max(0, payload.Length - values.Offset));
        var elements = (int)(bytes / child.Stride);
        state.Children[childIndex] = child.DecodeElements(payload.Slice(values.Offset, (int)bytes), elements, decoder.Order);
        if (target.CountField is { } countField && run.Plan.IndexOf(countField) is var ci and >= 0)
            state.Values[ci] = ColumnValue(run.Plan.Columns[ci], elements);
    }

    private sealed record TrackRun(TrackRecipe Recipe, MessagePlan Plan)
    {
        public Dictionary<long, TrackState> Live { get; } = new();
    }

    private sealed record TrackState(object?[] Values, List<object?[]>?[] Children);

    private sealed record NavRun(MessagePlan Plan, List<(string Role, FieldExpr.Bound Expr)> Fields);

    private sealed record BoundRule(EventType Type, Func<object?[], bool>? When, List<(string Property, FieldExpr.Bound Expr)> Fields);
}

using PMDA.Core.Events;
using PMDA.Formats.Decoding;
using PMDA.Formats.Demo;
using PMDA.Formats.Dialect;
using PMDA.Formats.Framing;
using PMDA.Formats.Projects;
using PMDA.Formats.Recipes;

namespace PMDA.Tests.Formats;

public class RecipeTests
{
    private static Dictionary<string, object> F(params (string Key, object Value)[] values) => values.ToDictionary(v => v.Key, v => v.Value);

    [Fact]
    public void Demo_project_compiles_cleanly()
    {
        var format = DemoFormat.Compile();
        Assert.True(format.CanImport, string.Join("\n", format.Diagnostics));
        Assert.DoesNotContain(format.Diagnostics, d => d.Severity == Severity.Error);
        Assert.Equal("threat_struct", format.TableByEvent.GetValueOrDefault(300));
        Assert.False(format.TableByEvent.ContainsKey(1)); // consumed by the track
        var stored = format.StoredTables.Select(p => p.Key).Order().ToList();
        Assert.Equal(["bit_struct", "cm_struct", "mode_struct", "nav_struct", "sync_struct", "threat_struct"], stored);
    }

    [Fact]
    public void Track_merges_lifecycle_lists_delete_code_and_nav()
    {
        var format = DemoFormat.Compile();
        var rows = new List<OutputRow>();
        var events = new List<FlightEvent>();
        var diagnostics = new CollectingDiagnostics(50);
        using var dump = DemoDump(format);
        var engine = new RecipeEngine(format);
        var clock = new FlightClock(new DateTime(2026, 3, 14), 0.001);
        long seq = 0;
        foreach (var r in RecordReader.Read(dump, format.Frame!, diagnostics, CancellationToken.None))
            engine.Process(r, clock.ToUtc(r.Time), seq++, rows.Add, events.Add);

        Assert.Contains(diagnostics.Items, d => d.Message.Contains("Lost record sync"));
        var threats = rows.Where(r => r.Plan.Key == "threat_struct").ToList();
        var plan = threats[0].Plan;
        object? V(OutputRow row, string field) => row.Values[plan.IndexOf(field)];

        Assert.Equal([0L, 1L, 2L, 3L, 2L], threats.Select(r => V(r, "status")));
        Assert.All(threats.Take(4), r => Assert.Equal(77, V(r, "threat_id")));

        // New: fields from the event, aircraft state from the latest nav record.
        Assert.Equal("ALPHA", V(threats[0], "site_name"));
        Assert.Equal(31.95, (double)V(threats[0], "own_lat")!, 6);
        Assert.Equal(15000.0, (double)V(threats[0], "own_alt")!, 6);

        // Extension: the list joins as child rows, with its count.
        var pris = threats[1].Children[plan.Children.ToList().FindIndex(c => c.Field == "pris")]!;
        Assert.Equal([250.0, 262.5], pris.Select(p => Math.Round((double)p[0]!, 3)));
        Assert.Equal(2, V(threats[1], "pri_count"));

        // Update: the event's values replace the old ones (it carries the whole report); the joined list stays; nav has moved.
        Assert.Equal(50.625, (double)V(threats[2], "aoa")!, 6);
        Assert.Equal(9401.0, (double)V(threats[2], "frq")!, 6);
        Assert.NotNull(threats[2].Children[plan.Children.ToList().FindIndex(c => c.Field == "pris")]);
        Assert.Equal(32.0, (double)V(threats[2], "own_lat")!, 6);

        // Delete: the delete code comes from the delete event.
        Assert.Equal(1L, V(threats[3], "drop_code"));

        // An update for a threat never announced still makes a row, and is counted.
        Assert.Equal(88, V(threats[4], "threat_id"));
        Assert.Equal(1, engine.Stats.UpdatesWithoutNew);
        Assert.Equal(1, engine.Stats.UnknownEvents[99]);

        // Common model: emitters except deletes, the launch warning, nav and the flare drop.
        Assert.Equal(4, events.Count(e => e.EventType == EventType.Emitter));
        var warning = Assert.Single(events, e => e.EventType == EventType.Warning);
        Assert.Equal((short)AlertKind.MissileLaunch, warning.SubType);
        Assert.Equal(77, warning.EmitterId);
        var nav = events.Where(e => e.EventType == EventType.Navigation).ToList();
        Assert.Equal(2, nav.Count);
        Assert.Equal(15000 * 0.3048, nav[0].AltM!.Value, 1);
        var cm = Assert.Single(events, e => e.EventType == EventType.Countermeasure);
        Assert.Equal((2, 2), ((int)cm.SubType!, cm.Count!.Value));
        var emitter = events.First(e => e.EventType == EventType.Emitter);
        Assert.Equal((77, 9400f), (emitter.EmitterId!.Value, emitter.FrequencyMHz!.Value));
        Assert.Equal(new DateTime(2026, 3, 14, 9, 30, 0, 200, DateTimeKind.Utc), emitter.TimeUtc);
    }

    [Fact]
    public void Flight_info_and_dry_run_read_the_dump()
    {
        var format = DemoFormat.Compile();
        using var dump = DemoDump(format);
        var report = FlightRun.DryRun(format, dump);
        Assert.True(report.Flight.DateFound);
        Assert.Equal(new DateTime(2026, 3, 14), report.Flight.Date);
        Assert.Equal("TAIL-42", report.Flight.Tail);
        Assert.Equal(10, report.Records);
        Assert.Equal(5, report.Stats.RowsByTable["threat_struct"]);
        Assert.Equal(new DateTime(2026, 3, 14, 9, 30, 0, DateTimeKind.Utc), report.FirstUtc);
    }

    [Fact]
    public void Clock_moves_to_the_next_day_at_midnight()
    {
        var clock = new FlightClock(new DateTime(2026, 3, 14), 0.001);
        Assert.Equal(new DateTime(2026, 3, 14, 23, 59, 59, DateTimeKind.Utc), clock.ToUtc(86_399_000));
        Assert.Equal(new DateTime(2026, 3, 15, 0, 0, 1, DateTimeKind.Utc), clock.ToUtc(1_000));
    }

    [Fact]
    public void Field_expressions_and_conditions_parse()
    {
        Assert.Equal(new FieldExpr("alt", 0.3048, 0), FieldExpr.Parse("alt*0.3048"));
        Assert.Equal(new FieldExpr("x", 0.5, -3), FieldExpr.Parse("x/2-3"));
        Assert.Equal(new FieldExpr("", 0, 3), FieldExpr.Parse("3"));
        Assert.Equal(new Condition("status", "!=", 3), Condition.Parse("status != 3"));
        Assert.Throws<FormatException>(() => FieldExpr.Parse("a+b"));
    }

    [Fact]
    public void Recipe_mistakes_are_reported()
    {
        var project = DemoFormat.Project with
        {
            Recipe = DemoFormat.Project.Recipe with
            {
                Bind = new() { ["1"] = "no_such_struct" },
                Tracks = [new TrackRecipe { Struct = "threat_struct", Key = "nope", New = [1] }],
            },
        };
        var format = CompiledFormat.Compile(project, DemoFormat.Header);
        Assert.False(format.CanImport);
        Assert.Contains(format.Diagnostics, d => d.Message.Contains("no_such_struct"));
        Assert.Contains(format.Diagnostics, d => d.Message.Contains("event 1 has no struct"));
    }

    /// <summary>A short demo flight: sync, nav, a threat's whole life, a countermeasure, an unknown event and some damage.</summary>
    private static MemoryStream DemoDump(CompiledFormat format)
    {
        var ms = new MemoryStream();
        var w = new RawDumpWriter(format, ms);
        const long t0 = 9 * 3_600_000 + 30 * 60_000;
        w.Write(12, t0, F(("event_code", 12), ("year", 26), ("month", 3), ("day", 14), ("tail_num", "TAIL-42"), ("sync_code", 0xABCDABCDL)));
        w.Write(13, t0 + 100, F(("event_code", 13), ("lat", 31.95), ("lon", 35.93), ("alt", 15000), ("hdg", 90.0)));
        w.Write(1, t0 + 200, F(("event_code", 1), ("threat_id", 77), ("aoa", 45.0), ("power", -60.0), ("frq", 9400.0), ("pri", 250.0), ("pw", 1.5), ("site_name", "ALPHA")));
        w.WriteList(2, t0 + 300, F(("event_code", 2), ("threat_id", 77), ("word_count", 4), ("list_kind", 0)), "threat_struct", "pris",
            [F(("pri_value", 250.0), ("pri_flags", 1)), F(("pri_value", 262.5), ("pri_flags", 0))]);
        ms.Write([0xDE, 0xAD, 0xBE, 0xEF, 0x01]); // damage between records
        w.Write(13, t0 + 400, F(("event_code", 13), ("lat", 32.0), ("lon", 36.0), ("alt", 16000), ("hdg", 95.0)));
        w.Write(3, t0 + 500, F(("event_code", 3), ("threat_id", 77), ("aoa", 50.625), ("power", -55.0), ("frq", 9401.0), ("launch", 1)));
        w.Write(20, t0 + 600, F(("event_code", 20), ("kind", 2), ("count", 2), ("flare_left", 28)));
        w.WriteRaw(99, t0 + 650, new byte[8]);
        w.Write(5, t0 + 700, F(("event_code", 5), ("threat_id", 77), ("reason", 1)));
        w.Write(3, t0 + 800, F(("event_code", 3), ("threat_id", 88), ("frq", 3000.0)));
        ms.Position = 0;
        return ms;
    }
}

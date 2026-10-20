using System.Text.Json;
using PMDA.Formats.Framing;
using PMDA.Formats.Projects;

namespace PMDA.Formats.Demo;

/// <summary>The invented demo project: a header in the vendor dialect and its recipe, for tests, the sample generator and first-run setup.</summary>
public static class DemoFormat
{
    public static string Header => Read("demo.h");

    public static FormatProject Project =>
        JsonSerializer.Deserialize<FormatProject>(Read("demo.project.json"), FormatProject.Json)!;

    public static CompiledFormat Compile() => CompiledFormat.Compile(Project, Header);

    /// <summary>
    /// A short recording: sync, nav, one threat's whole life (new, list extension, update with launch, delete), a
    /// flare drop, an update for a threat never announced, an event the header does not know and a few damaged bytes.
    /// </summary>
    public static byte[] ShortRecording(CompiledFormat? format = null, string tail = "TAIL-42")
    {
        format ??= Compile();
        static Dictionary<string, object> F(params (string Key, object Value)[] values) => values.ToDictionary(v => v.Key, v => v.Value);
        using var ms = new MemoryStream();
        var w = new RawDumpWriter(format, ms);
        const long t0 = 9 * 3_600_000 + 30 * 60_000; // 09:30:00.000, ms of day
        w.Write(12, t0, F(("event_code", 12), ("year", 26), ("month", 3), ("day", 14), ("tail_num", tail), ("sync_code", 0xABCDABCDL)));
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
        return ms.ToArray();
    }

    private static string Read(string name)
    {
        var assembly = typeof(DemoFormat).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith("." + name, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

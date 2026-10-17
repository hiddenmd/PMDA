// Writes a synthetic sample-format flight file for demos, manual testing and benchmarks.
//
//   dotnet run --project tools/PMDA.SampleGen -- <output> [--events N] [--corrupt N] [--seed N] [--tail TAIL]

using PMDA.Parsers.Sample;

if (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
{
    Console.Error.WriteLine("usage: PMDA.SampleGen <output> [--events N] [--corrupt N] [--seed N] [--tail TAIL]");
    return 1;
}

var output = args[0];
var events = IntOption("--events", 100_000);
var corrupt = IntOption("--corrupt", 0);
var seed = IntOption("--seed", Environment.TickCount);
var tail = StringOption("--tail", "TAIL-042");

var random = new Random(seed);
var start = new DateTime(2026, 3, 14, 9, 30, 0, DateTimeKind.Utc);
// Spread corrupt records evenly through the file.
var corruptEvery = corrupt > 0 ? Math.Max(1, events / corrupt) : int.MaxValue;

using (var file = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
{
    var w = new SampleFormatWriter(file);
    w.WriteHeader(start, tail, $"MSN-{seed % 10_000}");
    double lat = 44.43, lon = 26.10;
    for (var i = 0; i < events; i++)
    {
        var t = TimeSpan.FromMilliseconds(i * 5.0);
        if (i % corruptEvery == corruptEvery - 1)
            w.WriteRecord(SampleFormat.EmitterMessage, t, new byte[SampleEmitterDecoder.PayloadSize], corruptChecksum: true);
        else if (i % 50 == 0)
        {
            lat += 0.0005;
            lon += 0.0004;
            w.WriteNavigation(t, lat, lon, 3000 + random.Next(-50, 50));
        }
        else if (i % 997 == 0)
            w.WriteCountermeasure(t, (byte)random.Next(1, 3), (byte)random.Next(1, 5));
        else
            w.WriteEmitter(t, (uint)random.Next(1, 40), 8000 + random.Next(0, 4000), 500 + random.Next(0, 1500),
                (float)(0.5 + random.NextDouble() * 20), random.Next(0, 360), -30 - random.Next(0, 50));
    }
}

Console.WriteLine($"Wrote {events:N0} records ({corrupt} corrupt) to {output} ({new FileInfo(output).Length:N0} bytes).");
return 0;

int IntOption(string name, int fallback)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? int.Parse(args[i + 1]) : fallback;
}

string StringOption(string name, string fallback)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
}

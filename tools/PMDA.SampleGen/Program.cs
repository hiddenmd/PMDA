// Writes synthetic sample-format flight recordings.
//
//   dotnet run --project tools/PMDA.SampleGen -c Release -- --all samples
//   dotnet run --project tools/PMDA.SampleGen -c Release -- --scenario dense-threat samples/dense.bin
//   dotnet run --project tools/PMDA.SampleGen -c Release -- --scenario training-sortie out.bin --seed 7 --tail TRN-031 --mission TRAINING-7 --start 2026-02-03T08:15Z
//   dotnet run --project tools/PMDA.SampleGen -c Release -- --scenario desert-range out.raw --format demo-h
//   dotnet run --project tools/PMDA.SampleGen -c Release -- --list
//
// --seed changes the emitters and noise (and, for area scenarios, the route); --tail, --mission and --start
// change the header; together they make a different flight of the same kind. --format demo-h writes a raw recorder
// dump in the invented demo header's format (src/PMDA.Formats/Demo) instead of the sample format.

using System.Diagnostics;
using System.Globalization;
using PMDA.SampleGen;

if (args is ["--list"])
{
    foreach (var s in Scenario.Standard)
        Console.WriteLine($"{s.Name,-16} {s.Description}");
    return 0;
}

if (args is ["--all", var directory])
{
    Directory.CreateDirectory(directory);
    foreach (var s in Scenario.Standard)
        Generate(s, Path.Combine(directory, $"{s.Name}.bin"));
    return 0;
}

if (args is ["--scenario", var name, var output, .. var options])
{
    var scenario = Scenario.Standard.FirstOrDefault(s => s.Name == name);
    if (scenario is null)
    {
        Console.Error.WriteLine($"Unknown scenario '{name}'. Use --list.");
        return 1;
    }
    if (WithOptions(scenario, options) is not { } varied)
        return 1;
    Generate(varied, output);
    return 0;
}

Console.Error.WriteLine("usage: PMDA.SampleGen --all <dir> | --scenario <name> <file> [--seed n] [--tail t] [--mission m] [--start utc] [--format sample|demo-h] | --list");
return 1;

static Scenario? WithOptions(Scenario scenario, string[] options)
{
    for (var i = 0; i + 1 < options.Length; i += 2)
    {
        var value = options[i + 1];
        switch (options[i])
        {
            case "--seed" when int.TryParse(value, CultureInfo.InvariantCulture, out var seed):
                scenario = scenario with { Seed = seed };
                break;
            case "--format" when value is "sample" or "demo-h":
                scenario = scenario with { Format = value };
                break;
            case "--tail":
                scenario = scenario with { Tail = value };
                break;
            case "--mission":
                scenario = scenario with { Mission = value };
                break;
            case "--start" when DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var start):
                scenario = scenario with { StartUtc = DateTime.SpecifyKind(start, DateTimeKind.Utc) };
                break;
            default:
                Console.Error.WriteLine($"Bad option '{options[i]} {value}'.");
                return null;
        }
    }
    if (options.Length % 2 != 0)
    {
        Console.Error.WriteLine($"Option '{options[^1]}' needs a value.");
        return null;
    }
    return scenario;
}

static void Generate(Scenario scenario, string path)
{
    var watch = Stopwatch.StartNew();
    var result = new MissionSimulator(scenario).Write(path);
    Console.WriteLine($"{Path.GetFileName(path),-22} {result.Records,12:N0} records {result.Bytes / 1_048_576.0,8:N1} MB" +
                      $"{(result.Corrupt > 0 ? $"  {result.Corrupt} corrupt" : "")}  ({watch.Elapsed.TotalSeconds:N1} s)");
}

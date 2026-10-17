// Writes synthetic sample-format flight recordings.
//
//   dotnet run --project tools/PMDA.SampleGen -c Release -- --all samples
//   dotnet run --project tools/PMDA.SampleGen -c Release -- --scenario dense-threat samples/dense.bin
//   dotnet run --project tools/PMDA.SampleGen -c Release -- --list

using System.Diagnostics;
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

if (args is ["--scenario", var name, var output])
{
    var scenario = Scenario.Standard.FirstOrDefault(s => s.Name == name);
    if (scenario is null)
    {
        Console.Error.WriteLine($"Unknown scenario '{name}'. Use --list.");
        return 1;
    }
    Generate(scenario, output);
    return 0;
}

Console.Error.WriteLine("usage: PMDA.SampleGen --all <dir> | --scenario <name> <file> | --list");
return 1;

static void Generate(Scenario scenario, string path)
{
    var watch = Stopwatch.StartNew();
    var result = new MissionSimulator(scenario).Write(path);
    Console.WriteLine($"{Path.GetFileName(path),-22} {result.Records,12:N0} records {result.Bytes / 1_048_576.0,8:N1} MB" +
                      $"{(result.Corrupt > 0 ? $"  {result.Corrupt} corrupt" : "")}  ({watch.Elapsed.TotalSeconds:N1} s)");
}

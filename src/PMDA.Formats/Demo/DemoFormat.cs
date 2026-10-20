using System.Text.Json;
using PMDA.Formats.Projects;

namespace PMDA.Formats.Demo;

/// <summary>The invented demo project: a header in the vendor dialect and its recipe, for tests, the sample generator and first-run setup.</summary>
public static class DemoFormat
{
    public static string Header => Read("demo.h");

    public static FormatProject Project =>
        JsonSerializer.Deserialize<FormatProject>(Read("demo.project.json"), FormatProject.Json)!;

    public static CompiledFormat Compile() => CompiledFormat.Compile(Project, Header);

    private static string Read(string name)
    {
        var assembly = typeof(DemoFormat).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith("." + name, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

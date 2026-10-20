using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using PMDA.Core.Parsing;
using PMDA.Formats.Framing;
using PMDA.Formats.Projects;

namespace PMDA.Api.Formats;

public sealed class FormatsOptions
{
    public const string Section = "Formats";

    /// <summary>Where projects and their header versions are kept. Default: %ProgramData%\PMDA\formats.</summary>
    public string? Directory { get; set; }
}

public sealed record FormatVersionInfo(int Version, string Sha256, long Bytes, DateTime SavedUtc);

/// <summary>A project with its current header compiled.</summary>
public sealed record FormatCurrent(FormatProject Project, int Version, string Header, CompiledFormat Format)
{
    internal string ProjectJson { get; init; } = "";
}

/// <summary>
/// Header-defined projects on disk: <c>{dir}\{id}\project.json</c> and every uploaded header as
/// <c>{dir}\{id}\versions\NNN.h</c>. Vendor headers are written with <see cref="SecureFile"/>, readable by
/// administrators and the service only.
/// </summary>
public sealed partial class FormatProjectStore(IOptions<FormatsOptions> options, ILogger<FormatProjectStore> logger)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, FormatCurrent> _compiled = new(StringComparer.OrdinalIgnoreCase);

    public string Root => options.Value.Directory is { Length: > 0 } d
        ? d
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PMDA", "formats");

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{0,31}$")]
    public static partial Regex IdPattern();

    private string Dir(string id) => Path.Combine(Root, id);
    private string ProjectFile(string id) => Path.Combine(Dir(id), "project.json");
    private string VersionsDir(string id) => Path.Combine(Dir(id), "versions");
    private string VersionFile(string id, int version) => Path.Combine(VersionsDir(id), $"{version:000}.h");

    public IReadOnlyList<FormatProject> List()
    {
        if (!Directory.Exists(Root))
            return [];
        var projects = new List<FormatProject>();
        foreach (var dir in Directory.EnumerateDirectories(Root).Order(StringComparer.OrdinalIgnoreCase))
            if (Find(Path.GetFileName(dir)) is { } p)
                projects.Add(p);
        return projects;
    }

    public FormatProject? Find(string id)
    {
        if (!IdPattern().IsMatch(id) || !File.Exists(ProjectFile(id)))
            return null;
        try
        {
            return JsonSerializer.Deserialize<FormatProject>(File.ReadAllText(ProjectFile(id)), FormatProject.Json);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Format project {Id} could not be read", id);
            return null;
        }
    }

    public IReadOnlyList<FormatVersionInfo> Versions(string id)
    {
        if (!IdPattern().IsMatch(id) || !Directory.Exists(VersionsDir(id)))
            return [];
        return Directory.EnumerateFiles(VersionsDir(id), "*.h")
            .Select(f => (File: f, Ok: int.TryParse(Path.GetFileNameWithoutExtension(f), out var v), V: v))
            .Where(x => x.Ok)
            .OrderBy(x => x.V)
            .Select(x =>
            {
                var bytes = File.ReadAllBytes(x.File);
                return new FormatVersionInfo(x.V, Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length, File.GetLastWriteTimeUtc(x.File));
            })
            .ToList();
    }

    public string? Header(string id, int version) =>
        IdPattern().IsMatch(id) && File.Exists(VersionFile(id, version)) ? File.ReadAllText(VersionFile(id, version)) : null;

    /// <summary>The project compiled with its latest header; null when it has no header yet.</summary>
    public FormatCurrent? Current(string id)
    {
        if (Find(id) is not { } project)
            return null;
        var versions = Versions(id);
        if (versions.Count == 0)
            return null;
        var latest = versions[^1].Version;
        var json = JsonSerializer.Serialize(project, FormatProject.Json);
        lock (_gate)
        {
            if (_compiled.TryGetValue(id, out var cached) && cached.Version == latest && cached.ProjectJson == json)
                return cached;
        }
        var header = Header(id, latest)!;
        var current = new FormatCurrent(project, latest, header, CompiledFormat.Compile(project, header)) { ProjectJson = json };
        lock (_gate)
            _compiled[id] = current;
        return current;
    }

    /// <summary>Compiles a header for a project without saving anything.</summary>
    public static CompiledFormat Preview(FormatProject project, string header) => CompiledFormat.Compile(project, header);

    public void Save(FormatProject project)
    {
        if (!IdPattern().IsMatch(project.Id))
            throw new ArgumentException("The id is 1–32 letters, digits, '-' or '_'.", nameof(project));
        Directory.CreateDirectory(Dir(project.Id));
        SecureFile.WriteAllText(ProjectFile(project.Id), JsonSerializer.Serialize(project, FormatProject.Json));
        Invalidate(project.Id);
    }

    /// <summary>Stores a header as the project's next version; an unchanged header keeps the current version.</summary>
    public int AddVersion(string id, string header)
    {
        var versions = Versions(id);
        var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(header)));
        if (versions.Count > 0 && Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(VersionFile(id, versions[^1].Version)))) == sha)
            return versions[^1].Version;
        var next = versions.Count == 0 ? 1 : versions[^1].Version + 1;
        Directory.CreateDirectory(VersionsDir(id));
        SecureFile.WriteAllText(VersionFile(id, next), header);
        Invalidate(id);
        return next;
    }

    public bool Delete(string id)
    {
        if (!IdPattern().IsMatch(id) || !Directory.Exists(Dir(id)))
            return false;
        Directory.Delete(Dir(id), recursive: true);
        Invalidate(id);
        return true;
    }

    private void Invalidate(string id)
    {
        lock (_gate)
            _compiled.Remove(id);
    }

    /// <summary>
    /// The one project whose record frame reads the start of the file as a known message, or null when none or
    /// several do. The stream must be seekable; its position is restored.
    /// </summary>
    public FormatCurrent? Detect(Stream stream)
    {
        var start = stream.Position;
        FormatCurrent? match = null;
        try
        {
            foreach (var project in List())
            {
                if (Current(project.Id) is not { Format: { CanImport: true, Frame: { } frame } format } current)
                    continue;
                stream.Position = start;
                int hits = 0, read = 0;
                foreach (var record in RecordReader.Read(stream, frame, Quiet.Instance, CancellationToken.None).Take(8))
                {
                    read++;
                    if (format.Decoders.ContainsKey(record.EventId))
                        hits++;
                }
                // Most of the first records must be messages the header knows.
                if (read == 0 || hits * 2 <= read)
                    continue;
                if (match is not null)
                    return null; // ambiguous: the user picks
                match = current;
            }
            return match;
        }
        finally
        {
            stream.Position = start;
        }
    }

    private sealed class Quiet : IParseDiagnostics
    {
        public static readonly Quiet Instance = new();
        public void Report(long fileOffset, string message) { }
    }
}

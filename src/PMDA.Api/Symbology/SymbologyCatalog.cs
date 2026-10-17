using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace PMDA.Api.Symbology;

public sealed class SymbologyOptions
{
    public const string Section = "Symbology";

    /// <summary>Folder of profile files, relative to the application content root.</summary>
    public string Directory { get; set; } = "symbology";

    /// <summary>Profile used for customers without their own file.</summary>
    public string DefaultProfile { get; set; } = "default";
}

/// <summary>
/// Loads and validates the symbology profiles at start-up. An invalid file stops the server with a
/// message naming the file and the problem: better than an RWR silently showing wrong symbols.
/// </summary>
public sealed partial class SymbologyCatalog
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly Dictionary<string, SymbologyProfile> _profiles;
    private readonly string _defaultId;

    public SymbologyCatalog(IOptions<SymbologyOptions> options, IHostEnvironment environment)
        : this(Path.Combine(environment.ContentRootPath, options.Value.Directory), options.Value.DefaultProfile)
    {
    }

    public SymbologyCatalog(string directory, string defaultId)
    {
        if (!System.IO.Directory.Exists(directory))
            throw new InvalidOperationException($"Symbology folder '{directory}' does not exist.");

        _profiles = new Dictionary<string, SymbologyProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in System.IO.Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal))
        {
            var profile = Load(file);
            if (!_profiles.TryAdd(profile.Id, profile))
                throw new InvalidOperationException($"Symbology profile id '{profile.Id}' is defined twice ({Path.GetFileName(file)}).");
        }
        if (!_profiles.ContainsKey(defaultId))
            throw new InvalidOperationException($"Default symbology profile '{defaultId}' not found in '{directory}'.");
        _defaultId = defaultId;
    }

    public IReadOnlyCollection<SymbologyProfile> All => _profiles.Values;

    /// <summary>The profile of a customer, or the default one when the customer has none.</summary>
    public SymbologyProfile ForCustomer(string customerId) =>
        _profiles.TryGetValue(customerId, out var profile) ? profile : _profiles[_defaultId];

    public SymbologyProfile? Find(string id) => _profiles.GetValueOrDefault(id);

    public static SymbologyProfile Load(string file)
    {
        SymbologyProfile? profile;
        try
        {
            profile = JsonSerializer.Deserialize<SymbologyProfile>(File.ReadAllText(file), Json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Symbology profile {Path.GetFileName(file)} is not valid: {ex.Message}", ex);
        }
        var problems = Validate(profile!);
        if (!string.Equals(profile!.Id, Path.GetFileNameWithoutExtension(file), StringComparison.OrdinalIgnoreCase))
            problems.Add($"id '{profile.Id}' must match the file name");
        if (problems.Count > 0)
            throw new InvalidOperationException($"Symbology profile {Path.GetFileName(file)} is not valid: {string.Join("; ", problems)}.");
        return profile;
    }

    /// <returns>Every problem found (empty when valid).</returns>
    public static List<string> Validate(SymbologyProfile p)
    {
        var problems = new List<string>();
        if (!IdPattern().IsMatch(p.Id))
            problems.Add("id may only contain letters, digits, '-' and '_'");
        if (p.Display.Rings.Length == 0 || p.Display.Rings.Any(r => r is <= 0 or > 1))
            problems.Add("display.rings must be fractions between 0 and 1");
        if (p.Display.Rings.Zip(p.Display.Rings.Skip(1)).Any(x => x.Second <= x.First))
            problems.Add("display.rings must increase");
        if (p.Display.TickEveryDeg is < 5 or > 90 || 360 % p.Display.TickEveryDeg != 0)
            problems.Add("display.tickEveryDeg must divide 360 and be 5–90");
        if (p.Display.MaxSymbols is < 1 or > 64)
            problems.Add("display.maxSymbols must be 1–64");
        foreach (var (name, value) in new[] { ("critical", p.Placement.Critical), ("lethal", p.Placement.Lethal), ("nonLethal", p.Placement.NonLethal) })
        {
            if (value is <= 0 or > 1)
                problems.Add($"placement.{name} must be a fraction between 0 and 1");
        }
        foreach (var (rule, index) in p.Threats.Append(p.Unknown).Select((r, i) => (r, i)))
        {
            var where = index == p.Threats.Length ? "unknown" : $"threats[{index}]";
            if (string.IsNullOrWhiteSpace(rule.Code) || rule.Code.Length > 3)
                problems.Add($"{where}.code must be 1–3 characters");
            foreach (var (name, range) in new[] { ("frequencyMHz", rule.FrequencyMHz), ("priUs", rule.PriUs), ("pulseWidthUs", rule.PulseWidthUs) })
            {
                if (range is not null && (range.Length != 2 || range[0] > range[1]))
                    problems.Add($"{where}.{name} must be [min, max]");
            }
        }
        foreach (var cls in Enum.GetValues<ThreatClass>())
        {
            if (!p.Symbols.ByClass.ContainsKey(cls))
                problems.Add($"symbols.byClass needs an entry for {cls}");
        }
        if (p.Symbols.LockMinHitsPerSecond <= 0)
            problems.Add("symbols.lockMinHitsPerSecond must be positive");
        return problems;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex IdPattern();
}

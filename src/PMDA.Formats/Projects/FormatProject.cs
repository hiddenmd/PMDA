using System.Text.Json;
using System.Text.Json.Serialization;
using PMDA.Formats.Decoding;
using PMDA.Formats.Layout;

namespace PMDA.Formats.Projects;

/// <summary>
/// One recording project: its vendor header, how to read it, the recipe that replaces hard-coded merging, and
/// how fields map to the common event model. Everything here is data an administrator edits; no code per project.
/// </summary>
public sealed record FormatProject
{
    /// <summary>Short id, also the flights' customer id and the default SQL schema, e.g. "PROJ-A".</summary>
    public required string Id { get; init; }
    public required string Name { get; init; }
    public FormatSettings Settings { get; init; } = new();
    public Recipe Recipe { get; init; } = new();
    public List<EventRule> Events { get; init; } = new();

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };
}

public sealed record FormatSettings
{
    public ByteOrder ByteOrder { get; init; } = ByteOrder.LittleEndian;
    public BitOrder BitOrder { get; init; } = BitOrder.LsbFirst;
    /// <summary>Struct packing; null takes <c>#pragma pack</c> from the header, else natural alignment.</summary>
    public int? Pack { get; init; }
    /// <summary>
    /// Bytes every record header starts with, as hex. Null derives them from leading <c>&lt;Parse&gt;False</c>
    /// header fields named like <c>_signature5a1</c>; empty turns the check off.
    /// </summary>
    public string? SyncHex { get; init; }
    /// <summary>Seconds per tick of the <c>&lt;EventTime&gt;</c> field, which counts time of day.</summary>
    public double TimeUnitSeconds { get; init; } = 0.001;
    /// <summary>Flight date when no record carries one (yyyy-MM-dd).</summary>
    public string? DefaultDate { get; init; }
    public NamingOptions Naming { get; init; } = new();
    /// <summary>SQL schema for the typed tables; null uses a name made from the project id.</summary>
    public string? SqlSchema { get; init; }
}

/// <summary>Replaces the legacy hard-coded merging of raw recorder events.</summary>
public sealed record Recipe
{
    /// <summary>Event id → struct, for messages whose struct has no <c>&lt;ID&gt;</c>.</summary>
    public Dictionary<string, string> Bind { get; init; } = new();
    /// <summary>Struct → fields the recipe fills; they are not in the raw record's bytes.</summary>
    public Dictionary<string, List<string>> Derived { get; init; } = new();
    public List<TrackRecipe> Tracks { get; init; } = new();
    /// <summary>Where the aircraft state comes from, for attaching to tracked records.</summary>
    public NavSource? Nav { get; init; }
    /// <summary>Where the flight's date and tail come from.</summary>
    public FlightInfoSource? FlightInfo { get; init; }
}

/// <summary>
/// Follows one kind of object (an emitter) through new/update/extension/delete events by key and writes a row
/// per event into the struct's table, with status, delete code, joined lists and aircraft state filled in.
/// </summary>
public sealed record TrackRecipe
{
    public required string Struct { get; init; }
    /// <summary>Name for the derived table; null uses the struct's table name.</summary>
    public string? Table { get; init; }
    public required string Key { get; init; }
    public List<int> New { get; init; } = new();
    public List<int> Update { get; init; } = new();
    public List<int> Delete { get; init; } = new();
    public List<int> Extension { get; init; } = new();
    public StatusCodes? Status { get; init; }
    /// <summary>Field of the delete event copied to <see cref="DeleteCodeTo"/>.</summary>
    public string? DeleteCodeFrom { get; init; }
    public string? DeleteCodeTo { get; init; }
    public ExtensionJoin? Join { get; init; }
    /// <summary>Track field → nav role ("lat", "lon", "alt", "heading", "pitch", "roll").</summary>
    public Dictionary<string, string> AttachNav { get; init; } = new();
}

public sealed record StatusCodes
{
    public required string Field { get; init; }
    public int New { get; init; }
    public int Extension { get; init; } = 1;
    public int Update { get; init; } = 2;
    public int Delete { get; init; } = 3;
}

/// <summary>Extension records carry a list of values for an object: which list is chosen by <see cref="Kind"/>.</summary>
public sealed record ExtensionJoin
{
    public string Kind { get; init; } = "type";
    public string Count { get; init; } = "params_number";
    /// <summary>The array field holding the values.</summary>
    public string Values { get; init; } = "prm";
    /// <summary>"words": the count is in 32-bit words; "elements": in elements of the target list.</summary>
    public string CountUnit { get; init; } = "words";
    /// <summary>Kind value → target child list of the track struct.</summary>
    public Dictionary<string, JoinTarget> Lists { get; init; } = new();
}

public sealed record JoinTarget
{
    public required string Target { get; init; }
    /// <summary>Track field that receives the number of elements.</summary>
    public string? CountField { get; init; }
}

public sealed record NavSource
{
    /// <summary>Event id or struct name of the navigation record.</summary>
    public required string Message { get; init; }
    /// <summary>Role → field expression, e.g. "lat": "lat", "alt": "alt*0.3048".</summary>
    public Dictionary<string, string> Fields { get; init; } = new();
}

public sealed record FlightInfoSource
{
    public required string Message { get; init; }
    public string? Tail { get; init; }
    public string? Year { get; init; }
    public string? Month { get; init; }
    public string? Day { get; init; }
    public int YearBase { get; init; } = 2000;
}

public enum EventKind { Emitter = 1, Countermeasure = 2, Navigation = 3, Warning = 4, System = 5 }

/// <summary>
/// Maps rows of one message (a struct, a track or an event id) to the common event model, so the map, RWR,
/// timeline and alerts work for every project. Field expressions: <c>name</c>, <c>name*k</c>, <c>name*k+b</c>.
/// </summary>
public sealed record EventRule
{
    public required EventKind Kind { get; init; }
    public required string Message { get; init; }
    /// <summary>Optional filter, e.g. "status!=3".</summary>
    public string? When { get; init; }
    /// <summary>FlightEvent property → field expression, e.g. "FrequencyMHz": "frq".</summary>
    public Dictionary<string, string> Fields { get; init; } = new();
}

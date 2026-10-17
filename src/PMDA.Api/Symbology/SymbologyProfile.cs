namespace PMDA.Api.Symbology;

/// <summary>Kind of platform: decides the own-ship symbol and default layout.</summary>
public enum Platform
{
    FixedWing,
    Helicopter,
}

/// <summary>Threat classes an RWR distinguishes; each profile maps them to its own symbols.</summary>
public enum ThreatClass
{
    Sam,
    Aaa,
    Airborne,
    EarlyWarning,
    Naval,
    Unknown,
}

/// <summary>How dangerous a threat is; decides its ring when placement is by lethality.</summary>
public enum Lethality
{
    Critical,
    Lethal,
    NonLethal,
}

public enum PlacementMode
{
    /// <summary>Real RWR convention: rings by lethality, most dangerous nearest the centre.</summary>
    Lethality,
    /// <summary>Stronger signal nearer the centre.</summary>
    Amplitude,
}

/// <summary>Decoration drawn around or on a threat code.</summary>
public enum SymbolShape
{
    None,
    Hat,
    Boat,
    Diamond,
    Circle,
    Box,
    Shield,
}

/// <summary>
/// RWR symbology of one customer platform: display style, threat library and symbol set.
/// Loaded from <c>symbology/&lt;id&gt;.json</c>; a new platform is a new file, no rebuild.
/// </summary>
public sealed record SymbologyProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public Platform Platform { get; init; } = Platform.FixedWing;
    public required DisplayStyle Display { get; init; }
    public required Placement Placement { get; init; }
    /// <summary>Checked in order; the first matching rule classifies an emitter.</summary>
    public required ThreatRule[] Threats { get; init; }
    /// <summary>Used when no rule matches.</summary>
    public required ThreatRule Unknown { get; init; }
    public required SymbolSet Symbols { get; init; }
}

public sealed record DisplayStyle
{
    /// <summary>Phosphor colour of lines and text (CSS colour).</summary>
    public required string Phosphor { get; init; }
    public required string Background { get; init; }
    /// <summary>Bezel (frame) colour around the screen.</summary>
    public string Bezel { get; init; } = "#15171a";
    /// <summary>Range rings as fractions of the scope radius, inner to outer.</summary>
    public required double[] Rings { get; init; }
    /// <summary>Radius of the small circle around the own-ship symbol (fraction).</summary>
    public double OwnshipCircle { get; init; } = 0.17;
    public int TickEveryDeg { get; init; } = 30;
    /// <summary>Marks drawn at 3 and 9 o'clock, like the reference fighter format.</summary>
    public bool WingMarks { get; init; } = true;
    /// <summary>Legends around the screen edge, e.g. receiver name and mode.</summary>
    public Legend[] Legends { get; init; } = [];
    /// <summary>Countermeasure loads at take-off; remaining = load − dispensed. Null hides the counter.</summary>
    public CountermeasureLoad? Countermeasures { get; init; }
    /// <summary>Most symbols shown at once, highest priority first (real RWRs limit clutter too).</summary>
    public int MaxSymbols { get; init; } = 16;
}

public enum LegendPosition { TopLeft, Top, TopRight, BottomLeft, Bottom, BottomRight }

public sealed record Legend(LegendPosition Position, string Text, string? Sub = null, bool Boxed = false);

public sealed record CountermeasureLoad(int Chaff, int Flares);

public sealed record Placement
{
    public PlacementMode Mode { get; init; } = PlacementMode.Lethality;
    /// <summary>Ring radius (fraction) per lethality, when placing by lethality.</summary>
    public double Critical { get; init; } = 0.3;
    public double Lethal { get; init; } = 0.58;
    public double NonLethal { get; init; } = 0.86;
}

/// <summary>Classifies an emitter. Every given range must contain the value; omitted ranges match anything.</summary>
public sealed record ThreatRule
{
    /// <summary>Short display code, e.g. "08", "S", "A", "U" (max 3 characters).</summary>
    public required string Code { get; init; }
    public required ThreatClass Class { get; init; }
    public Lethality Lethality { get; init; } = Lethality.NonLethal;
    public string? Description { get; init; }
    public double[]? FrequencyMHz { get; init; }
    public double[]? PriUs { get; init; }
    public double[]? PulseWidthUs { get; init; }
    /// <summary>Explicit emitter ids, for libraries that identify emitters directly.</summary>
    public int[]? EmitterIds { get; init; }
}

public sealed record SymbolSet
{
    /// <summary>Decoration per threat class (e.g. airborne gets a hat, naval a boat).</summary>
    public required Dictionary<ThreatClass, SymbolShape> ByClass { get; init; }
    /// <summary>Marks the highest-priority threat.</summary>
    public SymbolShape Priority { get; init; } = SymbolShape.Diamond;
    /// <summary>Marks a threat that is tracking (locked on to) the aircraft.</summary>
    public SymbolShape Lock { get; init; } = SymbolShape.Circle;
    /// <summary>Detections per second above which an emitter counts as tracking (continuous illumination).</summary>
    public double LockMinHitsPerSecond { get; init; } = 12;
    /// <summary>A newly detected threat blinks for this long (flight time).</summary>
    public int NewThreatMs { get; init; } = 2500;
}

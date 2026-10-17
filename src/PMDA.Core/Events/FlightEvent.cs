namespace PMDA.Core.Events;

public enum EventType : byte
{
    Unknown = 0,
    Emitter = 1,
    Countermeasure = 2,
    Navigation = 3,
    Warning = 4,
    System = 5,
}

/// <summary>
/// One normalized event in the common PMDA model. Every customer format is decoded into this shape.
/// Fields that do not apply to an event type are null.
/// </summary>
public readonly record struct FlightEvent
{
    public Guid FlightId { get; init; }
    /// <summary>Order of the event within its flight, starting at 0.</summary>
    public long Seq { get; init; }
    public DateTime TimeUtc { get; init; }
    public EventType EventType { get; init; }
    /// <summary>Message-specific sub type, e.g. chaff vs flare for countermeasures.</summary>
    public short? SubType { get; init; }

    // Emitter parameters
    public int? EmitterId { get; init; }
    public float? FrequencyMHz { get; init; }
    public float? PriUs { get; init; }
    public float? PulseWidthUs { get; init; }
    public float? BearingDeg { get; init; }
    public float? AmplitudeDb { get; init; }

    // Position
    public double? Lat { get; init; }
    public double? Lon { get; init; }
    public float? AltM { get; init; }

    public int? Count { get; init; }
    /// <summary>Customer-specific leftover bytes that have no column in the common model.</summary>
    public byte[]? Extra { get; init; }
}

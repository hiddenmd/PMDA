namespace PMDA.Core.Events;

/// <summary>
/// Aural warnings the EW suite sounded in the cockpit. Stored as <see cref="EventType.Warning"/> events with the
/// kind in <see cref="FlightEvent.SubType"/>, the related emitter (if any) in EmitterId and its relative bearing
/// in BearingDeg. Customer formats map their own alert codes to these.
/// </summary>
public enum AlertKind : short
{
    Unknown = 0,
    /// <summary>A radar the RWR has not seen recently (the "new guy" tone).</summary>
    NewThreat = 1,
    /// <summary>A radar moved from search to track on the aircraft.</summary>
    RadarLock = 2,
    /// <summary>The RWR detected missile guidance from a tracking radar.</summary>
    MissileLaunch = 3,
    /// <summary>The missile warning system sees a missile closing.</summary>
    MissileApproach = 4,
    ChaffLow = 5,
    FlaresLow = 6,
    ChaffOut = 7,
    FlaresOut = 8,
}

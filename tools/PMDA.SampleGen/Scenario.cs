namespace PMDA.SampleGen;

/// <summary>What to simulate. All randomness derives from <see cref="Seed"/>, so files are reproducible.</summary>
internal sealed record Scenario(
    string Name,
    string Description,
    TimeSpan Duration,
    int Emitters,
    int Seed,
    string Tail,
    string Mission,
    int CorruptRecords = 0,
    bool TruncateEnd = false,
    int EmitterRateHz = 20)
{
    public static readonly Scenario[] Standard =
    [
        new("training-sortie", "20 min local training sortie, 3 emitters, a few countermeasures",
            TimeSpan.FromMinutes(20), Emitters: 3, Seed: 101, Tail: "TRN-012", Mission: "TRAINING-1"),
        new("dense-threat", "45 min ingress through a dense threat area, 30 emitters",
            TimeSpan.FromMinutes(45), Emitters: 30, Seed: 202, Tail: "ALPHA-07", Mission: "STRIKE-4"),
        new("corrupt-records", "10 min sortie with 25 corrupt records, garbage bytes and a truncated end",
            TimeSpan.FromMinutes(10), Emitters: 6, Seed: 303, Tail: "BRAVO-21", Mission: "RECCE-2",
            CorruptRecords: 25, TruncateEnd: true),
        new("long-mission", "6 h patrol over a large area, 120 emitters (~10 million records)",
            TimeSpan.FromHours(6), Emitters: 120, Seed: 404, Tail: "OSCAR-90", Mission: "PATROL-9", EmitterRateHz: 50),
    ];
}

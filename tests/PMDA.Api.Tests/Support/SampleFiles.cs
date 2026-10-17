using PMDA.Import;
using PMDA.Parsers;
using PMDA.Parsers.Sample;

namespace PMDA.Api.Tests.Support;

internal static class SampleFiles
{
    public static readonly DateTime Start = new(2026, 3, 14, 9, 30, 0, DateTimeKind.Utc);

    /// <summary>A sample-format flight; <paramref name="seed"/> makes the bytes (and hash) unique.</summary>
    public static byte[] Flight(int events = 10, int seed = 0, bool withBadRecord = false, string tail = "TAIL-042")
    {
        using var ms = new MemoryStream();
        var w = new SampleFormatWriter(ms);
        w.WriteHeader(Start, tail, $"MSN-{seed}");
        for (var i = 0; i < events; i++)
            w.WriteEmitter(TimeSpan.FromMilliseconds(i * 10), (uint)(seed * 1000 + i), 9000, 1000, 1, 90, -40);
        if (withBadRecord)
            w.WriteRecord(SampleFormat.CountermeasureMessage, TimeSpan.FromSeconds(60), [1, 1], corruptChecksum: true);
        return ms.ToArray();
    }

    public static FlightImporter Importer() =>
        new(new AdapterDetector([new SampleAdapter()]), new DecoderRegistry().AddSampleDecoders());
}

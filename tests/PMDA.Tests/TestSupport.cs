using PMDA.Core.Events;
using PMDA.Core.Import;
using PMDA.Core.Parsing;
using PMDA.Import;
using PMDA.Parsers;
using PMDA.Parsers.Sample;

namespace PMDA.Tests;

internal static class TestSupport
{
    public static readonly DateTime Start = new(2026, 3, 14, 9, 30, 0, DateTimeKind.Utc);

    public static MemoryStream SampleFile(Action<SampleFormatWriter> body)
    {
        var ms = new MemoryStream();
        var w = new SampleFormatWriter(ms);
        w.WriteHeader(Start, "TAIL-042", "MSN-7");
        body(w);
        ms.Position = 0;
        return ms;
    }

    public static FlightImporter Importer(DecoderRegistry? decoders = null) =>
        new(new AdapterDetector([new SampleAdapter()]), decoders ?? new DecoderRegistry().AddSampleDecoders());

    public static List<RawRecord> ReadAll(Stream s, ListDiagnostics diag)
    {
        var adapter = new SampleAdapter();
        var header = adapter.ReadHeader(s);
        // Copy payloads: they are only valid until the enumerator advances.
        return adapter.ReadRecords(s, header, diag, CancellationToken.None)
            .Select(r => r with { Payload = r.Payload.ToArray() })
            .ToList();
    }
}

internal sealed class ListDiagnostics : IParseDiagnostics
{
    public List<(long Offset, string Message)> Items { get; } = new();
    public void Report(long fileOffset, string message) => Items.Add((fileOffset, message));
}

internal sealed class MemorySink : IEventSink
{
    public List<FlightImportInfo> Begun { get; } = new();
    public List<FlightEvent> Events { get; } = new();
    public List<int> BatchSizes { get; } = new();
    public FlightImportSummary? Completed { get; private set; }
    public string? AbortReason { get; private set; }
    public HashSet<string> KnownHashes { get; } = new();
    public Func<int, Exception?>? FailOnBatch { get; init; }

    public Task<BeginFlightResult> BeginFlightAsync(FlightImportInfo info, CancellationToken ct)
    {
        if (info.FileSha256 is { } h && !KnownHashes.Add(Convert.ToHexString(h)))
            return Task.FromResult(BeginFlightResult.Duplicate);
        Begun.Add(info);
        return Task.FromResult(BeginFlightResult.Started);
    }

    public Task WriteBatchAsync(Guid flightId, ReadOnlyMemory<FlightEvent> events, CancellationToken ct)
    {
        if (FailOnBatch?.Invoke(BatchSizes.Count) is { } ex)
            throw ex;
        BatchSizes.Add(events.Length);
        Events.AddRange(events.ToArray());
        return Task.CompletedTask;
    }

    public Task CompleteFlightAsync(FlightImportSummary summary, CancellationToken ct)
    {
        Completed = summary;
        return Task.CompletedTask;
    }

    public Task AbortFlightAsync(Guid flightId, string reason, CancellationToken ct)
    {
        AbortReason = reason;
        Events.Clear();
        return Task.CompletedTask;
    }
}

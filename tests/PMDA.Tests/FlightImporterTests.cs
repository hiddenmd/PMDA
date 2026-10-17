using PMDA.Core.Events;
using PMDA.Import;
using PMDA.Parsers.Sample;
using static PMDA.Tests.TestSupport;

namespace PMDA.Tests;

public class FlightImporterTests
{
    [Fact]
    public async Task Imports_events_in_order_with_flight_id_and_sequence()
    {
        using var s = SampleFile(w =>
        {
            w.WriteNavigation(TimeSpan.FromSeconds(1), 45, 25, 1000);
            w.WriteEmitter(TimeSpan.FromSeconds(2), 1, 9000, 1000, 1, 90, -40);
            w.WriteCountermeasure(TimeSpan.FromSeconds(3), 2, 1);
        });
        var sink = new MemorySink();

        var result = await Importer().ImportAsync(s, "flight.bin", sink);

        Assert.Equal(ImportStatus.Completed, result.Status);
        Assert.Equal(3, result.EventCount);
        Assert.Equal(0, result.ErrorCount);
        Assert.Equal("TAIL-042", result.Header!.Tail);
        Assert.Equal([EventType.Navigation, EventType.Emitter, EventType.Countermeasure], sink.Events.Select(e => e.EventType));
        Assert.Equal([0L, 1L, 2L], sink.Events.Select(e => e.Seq));
        Assert.All(sink.Events, e => Assert.Equal(result.FlightId, e.FlightId));

        var info = Assert.Single(sink.Begun);
        Assert.Equal("flight.bin", info.SourceName);
        Assert.Equal(32, info.FileSha256!.Length);
        Assert.Equal(Start.AddSeconds(1), sink.Completed!.FirstEventUtc);
        Assert.Equal(Start.AddSeconds(3), sink.Completed.LastEventUtc);
        Assert.Equal(3, sink.Completed.EventCount);
    }

    [Fact]
    public async Task Writes_in_batches_of_configured_size()
    {
        using var s = SampleFile(w =>
        {
            for (var i = 0; i < 25; i++)
                w.WriteCountermeasure(TimeSpan.FromSeconds(i), 1, 1);
        });
        var sink = new MemorySink();

        await Importer().ImportAsync(s, "f", sink, new ImportOptions { BatchSize = 10 });

        Assert.Equal([10, 10, 5], sink.BatchSizes);
    }

    [Fact]
    public async Task Bad_and_unknown_records_are_counted_and_skipped()
    {
        using var s = SampleFile(w =>
        {
            w.WriteCountermeasure(TimeSpan.FromSeconds(1), 1, 1);
            w.WriteRecord(0x0099, TimeSpan.FromSeconds(2), [1, 2, 3]);           // unknown type
            w.WriteRecord(0x0099, TimeSpan.FromSeconds(3), [1, 2, 3]);           // reported once
            w.WriteRecord(SampleFormat.EmitterMessage, TimeSpan.FromSeconds(4), [1]); // bad payload
            w.WriteCountermeasure(TimeSpan.FromSeconds(5), 1, 1);
        });
        var sink = new MemorySink();

        var result = await Importer().ImportAsync(s, "f", sink);

        Assert.Equal(ImportStatus.Completed, result.Status);
        Assert.Equal(2, result.EventCount);
        Assert.Equal(2, result.UnknownMessageCount);
        Assert.Equal(2, result.ErrorCount);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("0x0099"));
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("Emitter payload"));
    }

    [Fact]
    public async Task Unrecognized_file_fails_without_touching_sink()
    {
        using var s = new MemoryStream(new byte[100]);
        var sink = new MemorySink();

        var result = await Importer().ImportAsync(s, "f", sink);

        Assert.Equal(ImportStatus.Failed, result.Status);
        Assert.Empty(sink.Begun);
    }

    [Fact]
    public async Task Same_file_twice_is_reported_as_duplicate()
    {
        var sink = new MemorySink();
        var bytes = SampleFile(w => w.WriteCountermeasure(TimeSpan.Zero, 1, 1)).ToArray();

        var first = await Importer().ImportAsync(new MemoryStream(bytes), "a", sink);
        var second = await Importer().ImportAsync(new MemoryStream(bytes), "b", sink);

        Assert.Equal(ImportStatus.Completed, first.Status);
        Assert.Equal(ImportStatus.Duplicate, second.Status);
        Assert.Single(sink.Events);
    }

    [Fact]
    public async Task Sink_failure_aborts_the_flight()
    {
        using var s = SampleFile(w =>
        {
            for (var i = 0; i < 5; i++)
                w.WriteCountermeasure(TimeSpan.FromSeconds(i), 1, 1);
        });
        var sink = new MemorySink { FailOnBatch = n => n == 1 ? new IOException("disk full") : null };

        var result = await Importer().ImportAsync(s, "f", sink, new ImportOptions { BatchSize = 2 });

        Assert.Equal(ImportStatus.Failed, result.Status);
        Assert.Contains("disk full", result.FailureReason);
        Assert.Contains("disk full", sink.AbortReason);
        Assert.Empty(sink.Events);
        Assert.Null(sink.Completed);
    }

    [Fact]
    public async Task Cancellation_aborts_the_flight_and_throws()
    {
        using var s = SampleFile(w => w.WriteCountermeasure(TimeSpan.Zero, 1, 1));
        var sink = new MemorySink();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Importer().ImportAsync(s, "f", sink, new ImportOptions { ComputeFileHash = false }, ct: cts.Token));

        Assert.Equal("Import cancelled.", sink.AbortReason);
    }

    [Fact]
    public async Task Forced_customer_id_skips_detection()
    {
        using var s = SampleFile(w => w.WriteCountermeasure(TimeSpan.Zero, 1, 1));

        var result = await Importer().ImportAsync(s, "f", new MemorySink(), new ImportOptions { CustomerId = "sample" });

        Assert.Equal(ImportStatus.Completed, result.Status);
        await Assert.ThrowsAsync<ArgumentException>(
            () => Importer().ImportAsync(s, "f", new MemorySink(), new ImportOptions { CustomerId = "nope" }));
    }

    [Fact]
    public void Schema_script_is_embedded()
    {
        var names = typeof(PMDA.Data.SchemaInstaller).Assembly.GetManifestResourceNames();
        Assert.Contains("PMDA.Data.Schema.001_init.sql", names);
    }
}

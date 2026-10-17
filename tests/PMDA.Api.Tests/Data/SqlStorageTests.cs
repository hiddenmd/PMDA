using PMDA.Api.Tests.Support;
using PMDA.Core.Events;
using PMDA.Core.Import;
using PMDA.Data;
using PMDA.Import;

namespace PMDA.Api.Tests.Data;

[Collection(SqlCollection.Name)]
public sealed class SqlStorageTests(SqlDatabase db) : IAsyncLifetime
{
    private readonly FlightRepository _flights = new(db.ConnectionString);

    public Task InitializeAsync() => db.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private Task<ImportResult> Import(byte[] file, IEventSink? sink = null, int batchSize = 102_400) =>
        SampleFiles.Importer().ImportAsync(
            new MemoryStream(file), "flight.bin", sink ?? new SqlEventSink(db.ConnectionString),
            new ImportOptions { BatchSize = batchSize });

    [Fact]
    public async Task Imported_flight_is_completed_with_all_events_stored()
    {
        var result = await Import(SampleFiles.Flight(events: 250), batchSize: 100);

        Assert.Equal(ImportStatus.Completed, result.Status);
        var flight = await _flights.GetAsync(result.FlightId, default);
        Assert.NotNull(flight);
        Assert.Equal(FlightStatus.Completed, flight.Status);
        Assert.Equal(250, flight.EventCount);
        Assert.Equal("TAIL-042", flight.Tail);
        Assert.Equal(SampleFiles.Start, flight.FirstEventUtc);
        Assert.Equal(250, await db.ScalarAsync("SELECT COUNT(*) FROM dbo.FlightEvent WHERE FlightId = @FlightId", result.FlightId));
    }

    [Fact]
    public async Task Same_file_twice_is_duplicate()
    {
        var file = SampleFiles.Flight();
        await Import(file);
        var second = await Import(file);

        Assert.Equal(ImportStatus.Duplicate, second.Status);
        Assert.Equal(1, await db.ScalarAsync("SELECT COUNT(*) FROM dbo.Flight"));
    }

    [Fact]
    public async Task Failed_import_leaves_flight_failed_without_events_and_can_be_retried()
    {
        var file = SampleFiles.Flight(events: 30);
        var failing = new FailingSink(new SqlEventSink(db.ConnectionString), failOnBatch: 2);

        var result = await Import(file, failing, batchSize: 10);

        Assert.Equal(ImportStatus.Failed, result.Status);
        var flight = await _flights.GetAsync(result.FlightId, default);
        Assert.Equal(FlightStatus.Failed, flight!.Status);
        Assert.Contains("boom", flight.FailureReason);
        Assert.Equal(0, await db.ScalarAsync("SELECT COUNT(*) FROM dbo.FlightEvent"));

        var retry = await Import(file);
        Assert.Equal(ImportStatus.Completed, retry.Status);
    }

    [Fact]
    public async Task List_pages_newest_first_and_filters_by_tail()
    {
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
            ids.Add((await Import(SampleFiles.Flight(seed: i, tail: i == 1 ? "OTHER" : "TAIL-042"))).FlightId);

        var page1 = await _flights.ListAsync(new FlightQuery(Page: 1, PageSize: 2), default);
        var page2 = await _flights.ListAsync(new FlightQuery(Page: 2, PageSize: 2), default);
        Assert.Equal(3, page1.Total);
        Assert.Equal(2, page1.Items.Count);
        Assert.Single(page2.Items);
        Assert.Equal(ids.AsEnumerable().Reverse(), page1.Items.Concat(page2.Items).Select(f => f.FlightId));

        var other = await _flights.ListAsync(new FlightQuery(Tail: "OTHER"), default);
        Assert.Equal(ids[1], Assert.Single(other.Items).FlightId);
    }

    [Fact]
    public async Task Interrupted_imports_are_failed_and_their_events_removed()
    {
        // A flight row left in Importing with some events, as a crashed server would leave it.
        var sink = new SqlEventSink(db.ConnectionString);
        var flightId = Guid.NewGuid();
        var header = new PMDA.Core.Parsing.FlightHeader("SAMPLE", 1, SampleFiles.Start, "T", "M");
        await sink.BeginFlightAsync(new FlightImportInfo(flightId, header, "crashed.bin", null), default);
        await sink.WriteBatchAsync(flightId, new[] { new FlightEvent { FlightId = flightId, TimeUtc = SampleFiles.Start } }, default);
        var completed = await Import(SampleFiles.Flight());

        var failed = await _flights.FailInterruptedImportsAsync(default);

        Assert.Equal(1, failed);
        var flight = await _flights.GetAsync(flightId, default);
        Assert.Equal(FlightStatus.Failed, flight!.Status);
        Assert.Equal(0, await db.ScalarAsync("SELECT COUNT(*) FROM dbo.FlightEvent WHERE FlightId = @FlightId", flightId));
        Assert.Equal(FlightStatus.Completed, (await _flights.GetAsync(completed.FlightId, default))!.Status);
    }

    [Fact]
    public async Task Deleting_hides_the_flight_at_once_and_purging_removes_its_data_in_batches()
    {
        var gone = (await Import(SampleFiles.Flight(events: 250, seed: 1))).FlightId;
        var keep = (await Import(SampleFiles.Flight(events: 10, seed: 2))).FlightId;
        await _flights.SaveDiagnosticsAsync(gone, [new ParseDiagnostic(1, "x")], default);

        var marked = await _flights.DeleteAsync([gone], default);

        Assert.Equal(1, marked);
        Assert.Null(await _flights.GetAsync(gone, default));
        Assert.Equal([keep], (await _flights.ListAsync(new FlightQuery(), default)).Items.Select(f => f.FlightId));
        Assert.Equal(250, await db.ScalarAsync("SELECT COUNT(*) FROM dbo.FlightEvent WHERE FlightId = @FlightId", gone)); // not yet purged

        var purged = await _flights.PurgeDeletedAsync(batchSize: 100, default);

        Assert.Equal(1, purged);
        Assert.Equal(0, await db.ScalarAsync("SELECT COUNT(*) FROM dbo.FlightEvent WHERE FlightId = @FlightId", gone));
        Assert.Equal(0, await db.ScalarAsync("SELECT COUNT(*) FROM dbo.ImportDiagnostic"));
        Assert.Equal(1, await db.ScalarAsync("SELECT COUNT(*) FROM dbo.Flight"));
        Assert.Equal(10, await db.ScalarAsync("SELECT COUNT(*) FROM dbo.FlightEvent"));
    }

    [Fact]
    public async Task Flights_still_importing_are_never_marked_deleted()
    {
        var importing = Guid.NewGuid();
        var sink = new SqlEventSink(db.ConnectionString);
        await sink.BeginFlightAsync(new FlightImportInfo(importing, new PMDA.Core.Parsing.FlightHeader("SAMPLE", 1, SampleFiles.Start, "T", "M"), "x.bin", null), default);

        Assert.Equal(0, await _flights.DeleteAsync([importing], default));
        Assert.Equal(1, await db.ScalarAsync("SELECT COUNT(*) FROM dbo.Flight WHERE Status = 0"));
    }

    [Fact]
    public async Task Unknown_flight_is_null()
    {
        Assert.Null(await _flights.GetAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Diagnostics_round_trip_in_offset_order()
    {
        var result = await Import(SampleFiles.Flight());
        await _flights.SaveDiagnosticsAsync(result.FlightId, [new ParseDiagnostic(900, "late"), new ParseDiagnostic(48, "early")], default);

        var read = await _flights.GetDiagnosticsAsync(result.FlightId, default);

        Assert.Equal([new DiagnosticRecord(48, "early"), new DiagnosticRecord(900, "late")], read);
    }

    private sealed class FailingSink(IEventSink inner, int failOnBatch) : IEventSink
    {
        private int _batches;
        public Task<BeginFlightResult> BeginFlightAsync(FlightImportInfo info, CancellationToken ct) => inner.BeginFlightAsync(info, ct);
        public Task WriteBatchAsync(Guid flightId, ReadOnlyMemory<FlightEvent> events, CancellationToken ct) =>
            ++_batches == failOnBatch ? throw new IOException("boom") : inner.WriteBatchAsync(flightId, events, ct);
        public Task CompleteFlightAsync(FlightImportSummary summary, CancellationToken ct) => inner.CompleteFlightAsync(summary, ct);
        public Task AbortFlightAsync(Guid flightId, string reason, CancellationToken ct) => inner.AbortFlightAsync(flightId, reason, ct);
    }
}

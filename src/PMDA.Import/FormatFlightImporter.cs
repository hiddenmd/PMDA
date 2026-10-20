using System.Security.Cryptography;
using PMDA.Core.Events;
using PMDA.Core.Import;
using PMDA.Core.Parsing;
using PMDA.Formats.Framing;
using PMDA.Formats.Projects;
using PMDA.Formats.Recipes;

namespace PMDA.Import;

/// <summary>
/// Imports a raw recorder dump of a header-defined project: frames records, runs the project's recipe, writes the
/// typed tables and raw records, and feeds the common event model for the map, RWR and timeline.
/// </summary>
public sealed class FormatFlightImporter
{
    public async Task<ImportResult> ImportAsync(
        Stream stream,
        string sourceName,
        CompiledFormat format,
        int version,
        string headerText,
        IEventSink sink,
        IFormatRowSink rows,
        ImportOptions? options = null,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        options ??= new ImportOptions();
        var flightId = Guid.NewGuid();
        var diagnostics = new CollectingDiagnostics(options.MaxReportedDiagnostics);
        if (!format.CanImport || format.Frame is not { } frame)
            return Failed(flightId, null, diagnostics, $"The {format.Project.Name} header has errors; fix them under Formats before importing.");
        if (!stream.CanSeek)
            throw new ArgumentException("Header-defined imports need a seekable stream.", nameof(stream));

        byte[]? hash = null;
        if (options.ComputeFileHash)
        {
            hash = await SHA256.HashDataAsync(stream, ct);
            stream.Position = 0;
        }

        var info = FlightRun.ScanInfo(format, stream);
        if (!info.DateFound)
            diagnostics.Report(0, $"No flight date in the recording; times are on {info.Date:yyyy-MM-dd}.");
        var startClock = new FlightClock(info.Date, format.Project.Settings.TimeUnitSeconds);
        var start = info.FirstTime is { } t ? startClock.ToUtc(t) : DateTime.SpecifyKind(info.Date, DateTimeKind.Utc);
        var header = new FlightHeader(format.Project.Id, version, start, info.Tail, Path.GetFileNameWithoutExtension(sourceName));

        PrepareResult prepared;
        try
        {
            prepared = await rows.PrepareAsync(format, version, headerText, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failed(flightId, null, diagnostics, $"Could not create the {format.Project.Name} tables: {ex.Message}");
        }

        var begin = await sink.BeginFlightAsync(new FlightImportInfo(flightId, header, sourceName, hash), ct);
        if (begin == BeginFlightResult.Duplicate)
            return new ImportResult(ImportStatus.Duplicate, flightId, header, 0, 0, 0, [], "This file was already imported.");
        foreach (var problem in prepared.Problems)
            diagnostics.Report(0, problem);

        var clock = new FlightClock(info.Date, format.Project.Settings.TimeUnitSeconds);
        var engine = new RecipeEngine(format);
        var batch = new FlightEvent[Math.Max(1, options.BatchSize)];
        var count = 0;
        long seq = 0, eventSeq = 0;
        DateTime? first = null, last = null;
        var pendingRows = new List<OutputRow>();
        var pendingEvents = new List<FlightEvent>();
        var reportedUnknown = new HashSet<int>();
        var reportedShort = new HashSet<int>();
        var totalBytes = stream.Length;

        try
        {
            foreach (var record in RecordReader.Read(stream, frame, diagnostics, ct))
            {
                var time = clock.ToUtc(record.Time);
                await rows.AddRawAsync(flightId, seq, time, record.EventId, record.Payload, ct);
                var outcome = engine.Process(record, time, seq, pendingRows.Add, pendingEvents.Add);
                if (outcome == RecordOutcome.Unknown && reportedUnknown.Add(record.EventId))
                    diagnostics.Report(record.FileOffset, $"Event {record.EventId} has no struct in the header; its records are kept raw only.");
                else if (engine.Stats.ShortByEvent.ContainsKey(record.EventId) && reportedShort.Add(record.EventId))
                    diagnostics.Report(record.FileOffset, $"Event {record.EventId} records are shorter than their struct; the missing fields are empty.");
                seq++;

                foreach (var row in pendingRows)
                    await rows.AddAsync(flightId, row, ct);
                pendingRows.Clear();

                foreach (var evt in pendingEvents)
                {
                    batch[count++] = evt with { FlightId = flightId, Seq = eventSeq++ };
                    first ??= evt.TimeUtc;
                    last = evt.TimeUtc;
                    if (count == batch.Length)
                    {
                        await sink.WriteBatchAsync(flightId, batch.AsMemory(0, count), ct);
                        count = 0;
                        progress?.Report(new ImportProgress(stream.Position, totalBytes, eventSeq, diagnostics.Count));
                    }
                }
                pendingEvents.Clear();

                if (seq % 50_000 == 0)
                    progress?.Report(new ImportProgress(stream.Position, totalBytes, eventSeq, diagnostics.Count));
            }
            if (count > 0)
                await sink.WriteBatchAsync(flightId, batch.AsMemory(0, count), ct);
            await rows.FlushAsync(ct);

            var errors = diagnostics.Count;
            await sink.CompleteFlightAsync(new FlightImportSummary(flightId, eventSeq, errors, first ?? start, last ?? start), ct);
            progress?.Report(new ImportProgress(totalBytes, totalBytes, eventSeq, errors));
            var unknown = engine.Stats.UnknownEvents.Values.Sum();
            return new ImportResult(ImportStatus.Completed, flightId, header, eventSeq, errors, unknown, diagnostics.Items, null);
        }
        catch (Exception ex)
        {
            var reason = ex is OperationCanceledException ? "Import cancelled." : $"Import failed: {ex.Message}";
            await rows.DeleteFlightAsync(flightId, CancellationToken.None);
            await sink.AbortFlightAsync(flightId, reason, CancellationToken.None);
            if (ex is OperationCanceledException)
                throw;
            return Failed(flightId, header, diagnostics, reason);
        }
    }

    private static ImportResult Failed(Guid flightId, FlightHeader? header, CollectingDiagnostics d, string reason) =>
        new(ImportStatus.Failed, flightId, header, 0, d.Count, 0, d.Items, reason);
}

using System.Collections.Concurrent;
using System.Security.Cryptography;
using PMDA.Core.Events;
using PMDA.Core.Import;
using PMDA.Core.Parsing;
using PMDA.Parsers;

namespace PMDA.Import;

public sealed record ImportOptions
{
    /// <summary>Skip auto-detection and use this customer's adapter.</summary>
    public string? CustomerId { get; init; }
    /// <summary>
    /// 102 400 is the smallest bulk insert SQL Server compresses straight into a columnstore
    /// rowgroup; smaller batches land in the slower delta store first.
    /// </summary>
    public int BatchSize { get; init; } = 102_400;
    /// <summary>
    /// Batches written at the same time, each on its own connection, while parsing carries on. SQL
    /// Server compresses each batch into a columnstore rowgroup on one core, and that compression is
    /// most of an import's cost, so parallel writes scale it across cores. 1 writes one at a time.
    /// </summary>
    public int MaxParallelWrites { get; init; } = 4;
    /// <summary>Hash the file first so the sink can reject re-imports. Costs one extra read.</summary>
    public bool ComputeFileHash { get; init; } = true;
    /// <summary>Diagnostics kept in the result; the counts include all of them.</summary>
    public int MaxReportedDiagnostics { get; init; } = 1_000;
}

public sealed record ImportProgress(long BytesRead, long TotalBytes, long EventCount, long ErrorCount);

public enum ImportStatus { Completed, Duplicate, Failed }

public sealed record ImportResult(
    ImportStatus Status,
    Guid FlightId,
    FlightHeader? Header,
    long EventCount,
    long ErrorCount,
    long UnknownMessageCount,
    IReadOnlyList<ParseDiagnostic> Diagnostics,
    string? FailureReason);

/// <summary>
/// Streams one flight file through adapter, decoders and sink. Bad records are reported and
/// skipped; a failure of the file or sink as a whole aborts the flight in the sink.
/// </summary>
public sealed class FlightImporter(AdapterDetector adapters, DecoderRegistry decoders)
{
    public async Task<ImportResult> ImportAsync(
        Stream stream,
        string sourceName,
        IEventSink sink,
        ImportOptions? options = null,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        options ??= new ImportOptions();
        if (options.BatchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "BatchSize must be positive.");
        if (options.MaxParallelWrites <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxParallelWrites must be positive.");

        var flightId = Guid.NewGuid();
        var diagnostics = new DiagnosticsCollector(options.MaxReportedDiagnostics);

        var adapter = options.CustomerId is { } id
            ? adapters.ById(id) ?? throw new ArgumentException($"No adapter for customer '{id}'.", nameof(options))
            : adapters.Detect(stream);
        if (adapter is null)
            return Failed(flightId, null, diagnostics, "File format not recognized by any customer adapter.");

        byte[]? hash = null;
        if (options.ComputeFileHash && stream.CanSeek)
        {
            var start = stream.Position;
            hash = await SHA256.HashDataAsync(stream, ct);
            stream.Position = start;
        }

        FlightHeader header;
        try
        {
            header = adapter.ReadHeader(stream);
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
        {
            return Failed(flightId, null, diagnostics, $"Invalid file header: {ex.Message}");
        }

        var begin = await sink.BeginFlightAsync(new FlightImportInfo(flightId, header, sourceName, hash), ct);
        if (begin == BeginFlightResult.Duplicate)
            return new ImportResult(ImportStatus.Duplicate, flightId, header, 0, 0, 0, [], "This file was already imported.");

        var table = decoders.For(adapter.CustomerId);
        await using var writer = new BatchWriter(sink, flightId, options.BatchSize, options.MaxParallelWrites, ct);
        var batch = writer.Rent();
        var count = 0;
        long seq = 0, unknown = 0;
        DateTime? first = null, last = null;
        var unknownTypes = new HashSet<ushort>();
        var totalBytes = stream.CanSeek ? stream.Length : -1;

        try
        {
            foreach (var record in adapter.ReadRecords(stream, header, diagnostics, ct))
            {
                var decoder = table[record.MessageType];
                if (decoder is null)
                {
                    unknown++;
                    if (unknownTypes.Add(record.MessageType))
                        diagnostics.Report(record.FileOffset, $"No decoder for message 0x{record.MessageType:X4}; such records are skipped.");
                    continue;
                }
                if (!decoder.TryDecode(record, out var evt, out var error))
                {
                    diagnostics.Report(record.FileOffset, error ?? "Payload could not be decoded.");
                    continue;
                }

                batch[count++] = evt with { FlightId = flightId, Seq = seq++ };
                first ??= evt.TimeUtc;
                last = evt.TimeUtc;

                if (count == batch.Length)
                {
                    await writer.WriteAsync(batch, count);
                    batch = writer.Rent();
                    count = 0;
                    progress?.Report(new ImportProgress(stream.CanSeek ? stream.Position : -1, totalBytes, seq, diagnostics.Count));
                }
            }
            if (count > 0)
                await writer.WriteAsync(batch, count);
            await writer.DrainAsync();

            var errors = diagnostics.Count;
            await sink.CompleteFlightAsync(new FlightImportSummary(flightId, seq, errors, first, last), ct);
            progress?.Report(new ImportProgress(totalBytes, totalBytes, seq, errors));
            return new ImportResult(ImportStatus.Completed, flightId, header, seq, errors, unknown, diagnostics.Items, null);
        }
        catch (Exception ex)
        {
            // Nothing may still be writing when the flight's events are removed.
            await writer.CancelAsync();
            var reason = ex is OperationCanceledException ? "Import cancelled." : $"Import failed: {ex.Message}";
            // Clean up even when the caller's token is cancelled.
            await sink.AbortFlightAsync(flightId, reason, CancellationToken.None);
            if (ex is OperationCanceledException)
                throw;
            return Failed(flightId, header, diagnostics, reason);
        }
    }

    private static ImportResult Failed(Guid flightId, FlightHeader? header, DiagnosticsCollector d, string reason) =>
        new(ImportStatus.Failed, flightId, header, 0, d.Count, 0, d.Items, reason);

    /// <summary>
    /// Writes full batches with at most <c>maxParallel</c> in flight, recycling their buffers. A write
    /// failure surfaces from the next <see cref="WriteAsync"/> or <see cref="DrainAsync"/>.
    /// </summary>
    private sealed class BatchWriter(IEventSink sink, Guid flightId, int batchSize, int maxParallel, CancellationToken ct) : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        private readonly List<Task> _inFlight = new();
        private readonly ConcurrentBag<FlightEvent[]> _free = new();

        public FlightEvent[] Rent() => _free.TryTake(out var batch) ? batch : new FlightEvent[batchSize];

        /// <summary>Starts writing <paramref name="batch"/>, first waiting for a slot if all are busy. The batch belongs to the writer now.</summary>
        public async Task WriteAsync(FlightEvent[] batch, int count)
        {
            while (_inFlight.Count >= maxParallel)
                await CompleteOneAsync();
            _inFlight.Add(WriteOneAsync(batch, count));
        }

        public async Task DrainAsync()
        {
            while (_inFlight.Count > 0)
                await CompleteOneAsync();
        }

        /// <summary>Cancels the writes still running and waits for them, ignoring their errors.</summary>
        public async Task CancelAsync()
        {
            await _cancel.CancelAsync();
            try
            {
                await Task.WhenAll(_inFlight);
            }
            catch
            {
                // the import already failed for its own reason
            }
            _inFlight.Clear();
        }

        public async ValueTask DisposeAsync()
        {
            if (_inFlight.Count > 0)
                await CancelAsync();
            _cancel.Dispose();
        }

        private async Task WriteOneAsync(FlightEvent[] batch, int count)
        {
            await sink.WriteBatchAsync(flightId, batch.AsMemory(0, count), _cancel.Token);
            _free.Add(batch);
        }

        private async Task CompleteOneAsync()
        {
            var done = await Task.WhenAny(_inFlight);
            _inFlight.Remove(done);
            await done; // rethrows its failure
        }
    }

    private sealed class DiagnosticsCollector(int max) : IParseDiagnostics
    {
        private readonly List<ParseDiagnostic> _items = new();
        public long Count { get; private set; }
        public IReadOnlyList<ParseDiagnostic> Items => _items;

        public void Report(long fileOffset, string message)
        {
            Count++;
            if (_items.Count < max)
                _items.Add(new ParseDiagnostic(fileOffset, message));
        }
    }
}

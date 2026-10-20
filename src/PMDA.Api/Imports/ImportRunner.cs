using Microsoft.Extensions.Options;
using PMDA.Api.Database;
using PMDA.Api.Formats;
using PMDA.Data;
using PMDA.Data.Formats;
using PMDA.Import;
using PMDA.Parsers;
using PMDA.Parsers.Sample;

namespace PMDA.Api.Imports;

/// <summary>Runs one queued job to a final state and always removes its uploaded file.</summary>
public sealed class ImportRunner(
    FlightImporter importer,
    FormatFlightImporter formatImporter,
    FormatProjectStore formats,
    AdapterDetector adapters,
    FlightStores stores,
    IOptions<ImportsOptions> options,
    ILogger<ImportRunner> logger)
{
    // One budget of bulk-write streams for all imports; a lone import may use all of it.
    private readonly SemaphoreSlim _writeGate = new(options.Value.SqlWriteStreams);

    public async Task RunAsync(ImportJob job, CancellationToken stopping)
    {
        try
        {
            if (!job.TryStart())
                return; // cancelled while queued

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(job.Cancellation, stopping);
            try
            {
                await ImportAsync(job, linked.Token);
            }
            catch (OperationCanceledException) when (job.Cancellation.IsCancellationRequested)
            {
                job.MarkCancelled();
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                job.Fail("The server stopped during the import.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Import {ImportId} ({FileName}) failed", job.Id, job.FileName);
                job.Fail($"Unexpected error: {ex.Message}");
            }
        }
        finally
        {
            DeleteUpload(job);
        }
    }

    private async Task ImportAsync(ImportJob job, CancellationToken ct)
    {
        // The database may have been removed or switched off since the upload was queued.
        if (stores.Get(job.DatabaseId) is not { Usable: true, ReadOnly: false } store)
        {
            job.Fail("The database this import was going to was removed, disabled or made read-only.");
            return;
        }
        var flights = store.Flights;
        ImportResult result;
        await using (var file = new FileStream(job.TempPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            var sink = new SqlEventSink(store.ConnectionString!, _writeGate);
            var importOptions = new ImportOptions { MaxParallelWrites = options.Value.SqlWriteStreams };
            var progress = new InlineProgress(job.ReportProgress);
            if (HeaderDefined(job, file) is { } format)
            {
                var rows = new SqlFormatRowSink(store.ConnectionString!, _writeGate);
                result = await formatImporter.ImportAsync(file, job.FileName, format.Format, format.Version, format.Header, sink, rows, importOptions, progress, ct);
            }
            else
            {
                result = await importer.ImportAsync(file, job.FileName, sink, importOptions with
                {
                    CustomerId = job.FormatId is { } id && id.Equals(SampleFormat.CustomerId, StringComparison.OrdinalIgnoreCase) ? SampleFormat.CustomerId : null,
                }, progress, ct);
            }
        }

        // A header means the sink created the flight row, so diagnostics have a flight to belong to.
        var flightExists = result.Header is not null && result.Status != ImportStatus.Duplicate;
        if (flightExists)
            stores.Remember(result.FlightId, store.Id);
        if (flightExists && result.Diagnostics.Count > 0)
            await flights.SaveDiagnosticsAsync(result.FlightId, result.Diagnostics, ct);

        switch (result.Status)
        {
            case ImportStatus.Completed:
                job.Complete(result.FlightId, result.EventCount, result.ErrorCount);
                break;
            case ImportStatus.Duplicate:
                job.MarkDuplicate();
                break;
            default:
                job.Fail(result.FailureReason ?? "Import failed.", flightExists ? result.FlightId : null);
                break;
        }
    }

    /// <summary>
    /// The header-defined project for this upload: the one chosen, else the one whose records the file starts with when
    /// no built-in format recognizes it. Null means the built-in importer handles it.
    /// </summary>
    private FormatCurrent? HeaderDefined(ImportJob job, Stream file)
    {
        if (job.FormatId is { } id)
            return id.Equals(SampleFormat.CustomerId, StringComparison.OrdinalIgnoreCase) ? null : formats.Current(id);
        if (adapters.Detect(file) is not null)
            return null;
        return formats.Detect(file);
    }

    private void DeleteUpload(ImportJob job)
    {
        try
        {
            File.Delete(job.TempPath);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Could not delete upload {Path}", job.TempPath);
        }
    }

    /// <summary>
    /// Reports on the calling thread. <see cref="Progress{T}"/> posts asynchronously, so a late
    /// report could arrive after the job finished.
    /// </summary>
    private sealed class InlineProgress(Action<ImportProgress> report) : IProgress<ImportProgress>
    {
        public void Report(ImportProgress value) => report(value);
    }
}

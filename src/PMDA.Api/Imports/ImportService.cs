using Microsoft.Extensions.Options;
using PMDA.Api.Database;

namespace PMDA.Api.Imports;

public enum SubmitStatus { Queued, QueueFull }

public sealed record SubmitResult(SubmitStatus Status, ImportJob? Job);

/// <summary>Application service behind the imports endpoints.</summary>
public sealed class ImportService(
    ImportJobStore store,
    ImportQueue queue,
    ImportNotifier notifier,
    IOptions<ImportsOptions> options,
    TimeProvider time)
{
    public async Task<SubmitResult> SubmitAsync(Stream content, string fileName, long sizeBytes, FlightStore target, CancellationToken ct, string? formatId = null)
    {
        var id = Guid.NewGuid();
        var directory = options.Value.ResolvedTempDirectory;
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $"{id:N}.upload");

        try
        {
            await using var file = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
            await content.CopyToAsync(file, ct);
        }
        catch
        {
            File.Delete(tempPath);
            throw;
        }

        var job = new ImportJob(id, Path.GetFileName(fileName), sizeBytes, tempPath, time, target.Id, target.Name) { FormatId = formatId };
        notifier.Watch(job);
        // In the store before the worker can see it, so GET never misses a running job.
        store.Add(job);
        if (!queue.TryEnqueue(job))
        {
            store.Remove(job.Id);
            File.Delete(tempPath);
            return new SubmitResult(SubmitStatus.QueueFull, null);
        }
        return new SubmitResult(SubmitStatus.Queued, job);
    }

    public ImportJobSnapshot? Find(Guid id) => store.Find(id)?.Snapshot();

    public IReadOnlyList<ImportJobSnapshot> List() => store.List();

    /// <returns>Null when the job does not exist.</returns>
    public (CancelRequest Result, ImportJobSnapshot Job)? Cancel(Guid id)
    {
        if (store.Find(id) is not { } job)
            return null;
        var result = job.RequestCancel();
        return (result, job.Snapshot());
    }
}

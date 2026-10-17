using System.Collections.Concurrent;

namespace PMDA.Api.Imports;

/// <summary>Jobs of this server process. Lost on restart; imported flights live in SQL.</summary>
public sealed class ImportJobStore
{
    private readonly ConcurrentDictionary<Guid, ImportJob> _jobs = new();

    public void Add(ImportJob job)
    {
        if (!_jobs.TryAdd(job.Id, job))
            throw new InvalidOperationException($"Import {job.Id} already exists.");
    }

    public void Remove(Guid id) => _jobs.TryRemove(id, out _);

    /// <summary>True while any import is queued or running.</summary>
    public bool HasActiveJobs() => _jobs.Values.Any(j => !ImportStates.IsFinal(j.State));

    public ImportJob? Find(Guid id) => _jobs.GetValueOrDefault(id);

    public IReadOnlyList<ImportJobSnapshot> List() =>
        _jobs.Values.Select(j => j.Snapshot()).OrderByDescending(s => s.CreatedUtc).ToList();
}

using PMDA.Import;

namespace PMDA.Api.Imports;

public enum CancelRequest
{
    /// <summary>The job had not started and is now cancelled.</summary>
    Cancelled,
    /// <summary>The job is running; it was signalled and becomes Cancelled when it stops.</summary>
    Signalled,
    AlreadyFinished,
}

public sealed record ImportJobSnapshot(
    Guid Id,
    string FileName,
    long SizeBytes,
    ImportState State,
    DateTime CreatedUtc,
    DateTime? StartedUtc,
    DateTime? FinishedUtc,
    ImportProgress? Progress,
    Guid? FlightId,
    string? FailureReason);

/// <summary>
/// One import and its lifecycle. All state changes go through <see cref="ImportStates"/>;
/// an illegal transition throws. Thread-safe. <see cref="Changed"/> fires after every change,
/// outside the lock.
/// </summary>
public sealed class ImportJob
{
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly TimeProvider _time;

    private ImportState _state = ImportState.Queued;
    private DateTime? _startedUtc, _finishedUtc;
    private ImportProgress? _progress;
    private Guid? _flightId;
    private string? _failureReason;

    public ImportJob(Guid id, string fileName, long sizeBytes, string tempPath, TimeProvider time)
    {
        Id = id;
        FileName = fileName;
        SizeBytes = sizeBytes;
        TempPath = tempPath;
        _time = time;
        CreatedUtc = time.GetUtcNow().UtcDateTime;
    }

    public Guid Id { get; }
    public string FileName { get; }
    public long SizeBytes { get; }
    /// <summary>Uploaded file waiting to be imported; deleted by the runner when done.</summary>
    public string TempPath { get; }
    public DateTime CreatedUtc { get; }
    public CancellationToken Cancellation => _cancellation.Token;

    public ImportState State
    {
        get { lock (_gate) return _state; }
    }

    public event Action<ImportJob>? Changed;

    /// <summary>Queued → Running. False when the job was cancelled before the worker got to it.</summary>
    public bool TryStart()
    {
        lock (_gate)
        {
            if (_state != ImportState.Queued)
                return false;
            ApplyLocked(ImportState.Running, () => _startedUtc = Now);
        }
        Changed?.Invoke(this);
        return true;
    }

    public void ReportProgress(ImportProgress progress)
    {
        lock (_gate)
        {
            if (_state != ImportState.Running)
                throw new InvalidOperationException($"Cannot report progress while {_state}.");
            _progress = progress;
        }
        Changed?.Invoke(this);
    }

    public void Complete(Guid flightId, long eventCount, long errorCount) => Transition(ImportState.Completed, () =>
    {
        _flightId = flightId;
        _progress = new ImportProgress(SizeBytes, SizeBytes, eventCount, errorCount);
    });

    public void MarkDuplicate() => Transition(ImportState.Duplicate, () => _failureReason = "This file was already imported.");

    public void Fail(string reason, Guid? flightId = null) => Transition(ImportState.Failed, () =>
    {
        _failureReason = reason;
        _flightId = flightId;
    });

    public void MarkCancelled() => Transition(ImportState.Cancelled, () => _failureReason = "Import cancelled.");

    public CancelRequest RequestCancel()
    {
        lock (_gate)
        {
            switch (_state)
            {
                case ImportState.Running:
                    _cancellation.Cancel();
                    return CancelRequest.Signalled;
                case ImportState.Queued:
                    _cancellation.Cancel();
                    ApplyLocked(ImportState.Cancelled, () => _failureReason = "Import cancelled.");
                    break;
                default:
                    return CancelRequest.AlreadyFinished;
            }
        }
        Changed?.Invoke(this);
        return CancelRequest.Cancelled;
    }

    public ImportJobSnapshot Snapshot()
    {
        lock (_gate)
            return new ImportJobSnapshot(Id, FileName, SizeBytes, _state, CreatedUtc, _startedUtc, _finishedUtc, _progress, _flightId, _failureReason);
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    private void Transition(ImportState to, Action apply)
    {
        lock (_gate)
            ApplyLocked(to, apply);
        Changed?.Invoke(this);
    }

    private void ApplyLocked(ImportState to, Action apply)
    {
        if (!ImportStates.CanTransition(_state, to))
            throw new InvalidOperationException($"Import {Id} cannot go from {_state} to {to}.");
        apply();
        _state = to;
        if (ImportStates.IsFinal(to))
            _finishedUtc = Now;
    }
}

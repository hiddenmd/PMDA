using Microsoft.Extensions.Time.Testing;
using PMDA.Api.Imports;
using PMDA.Import;

namespace PMDA.Api.Tests.Imports;

public class ImportJobTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 17, 8, 0, 0, TimeSpan.Zero));

    private ImportJob NewJob() => new(Guid.NewGuid(), "f.bin", 1000, "tmp", _time);

    [Fact]
    public void Happy_path_raises_changed_per_step_and_sets_timestamps()
    {
        var job = NewJob();
        var seen = new List<ImportState>();
        job.Changed += j => seen.Add(j.State);

        Assert.True(job.TryStart());
        _time.Advance(TimeSpan.FromSeconds(5));
        job.ReportProgress(new ImportProgress(500, 1000, 10, 0));
        var flightId = Guid.NewGuid();
        job.Complete(flightId, 20, 1);

        Assert.Equal([ImportState.Running, ImportState.Running, ImportState.Completed], seen);
        var s = job.Snapshot();
        Assert.Equal(ImportState.Completed, s.State);
        Assert.Equal(flightId, s.FlightId);
        Assert.Equal(new ImportProgress(1000, 1000, 20, 1), s.Progress);
        Assert.Equal(_time.GetUtcNow().UtcDateTime.AddSeconds(-5), s.StartedUtc);
        Assert.Equal(_time.GetUtcNow().UtcDateTime, s.FinishedUtc);
    }

    public static TheoryData<ImportState, ImportState> IllegalTransitions()
    {
        var data = new TheoryData<ImportState, ImportState>();
        foreach (var from in Enum.GetValues<ImportState>())
            foreach (var to in Enum.GetValues<ImportState>())
                if (!ImportStates.CanTransition(from, to))
                    data.Add(from, to);
        return data;
    }

    [Theory]
    [MemberData(nameof(IllegalTransitions))]
    public void Illegal_transition_throws_and_keeps_state(ImportState from, ImportState to)
    {
        var job = JobIn(from);
        Action? act = to switch
        {
            ImportState.Completed => () => job.Complete(Guid.NewGuid(), 0, 0),
            ImportState.Duplicate => job.MarkDuplicate,
            ImportState.Failed => () => job.Fail("x"),
            ImportState.Cancelled => job.MarkCancelled,
            ImportState.Running => () => Assert.False(job.TryStart()),
            _ => null, // nothing transitions back to Queued
        };
        if (act is null)
            return;

        if (to == ImportState.Running)
            act();
        else
            Assert.Throws<InvalidOperationException>(act);
        Assert.Equal(from, job.State);
    }

    [Fact]
    public void Progress_is_only_accepted_while_running()
    {
        var job = NewJob();
        Assert.Throws<InvalidOperationException>(() => job.ReportProgress(new ImportProgress(0, 0, 0, 0)));
    }

    [Fact]
    public void Cancel_while_queued_cancels_immediately_and_worker_skips_it()
    {
        var job = NewJob();

        Assert.Equal(CancelRequest.Cancelled, job.RequestCancel());
        Assert.Equal(ImportState.Cancelled, job.State);
        Assert.True(job.Cancellation.IsCancellationRequested);
        Assert.False(job.TryStart());
    }

    [Fact]
    public void Cancel_while_running_signals_and_waits_for_runner()
    {
        var job = NewJob();
        job.TryStart();

        Assert.Equal(CancelRequest.Signalled, job.RequestCancel());
        Assert.True(job.Cancellation.IsCancellationRequested);
        Assert.Equal(ImportState.Running, job.State);

        job.MarkCancelled();
        Assert.Equal(ImportState.Cancelled, job.State);
    }

    [Theory]
    [InlineData(ImportState.Completed)]
    [InlineData(ImportState.Duplicate)]
    [InlineData(ImportState.Failed)]
    [InlineData(ImportState.Cancelled)]
    public void Cancel_after_finish_reports_already_finished(ImportState final)
    {
        var job = JobIn(final);
        Assert.Equal(CancelRequest.AlreadyFinished, job.RequestCancel());
        Assert.Equal(final, job.State);
    }

    [Fact]
    public void Concurrent_cancel_and_start_end_in_a_consistent_state()
    {
        for (var i = 0; i < 1000; i++)
        {
            var job = NewJob();
            var started = false;
            CancelRequest cancel = default;
            Parallel.Invoke(() => started = job.TryStart(), () => cancel = job.RequestCancel());

            if (started)
            {
                Assert.Equal(CancelRequest.Signalled, cancel);
                Assert.Equal(ImportState.Running, job.State);
            }
            else
            {
                Assert.Equal(CancelRequest.Cancelled, cancel);
                Assert.Equal(ImportState.Cancelled, job.State);
            }
        }
    }

    private ImportJob JobIn(ImportState state)
    {
        var job = NewJob();
        if (state == ImportState.Queued)
            return job;
        if (state == ImportState.Cancelled)
        {
            job.RequestCancel();
            return job;
        }
        job.TryStart();
        switch (state)
        {
            case ImportState.Completed: job.Complete(Guid.NewGuid(), 0, 0); break;
            case ImportState.Duplicate: job.MarkDuplicate(); break;
            case ImportState.Failed: job.Fail("x"); break;
        }
        return job;
    }
}

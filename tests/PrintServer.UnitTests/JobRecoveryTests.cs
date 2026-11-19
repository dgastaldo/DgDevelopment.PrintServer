using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PrintServer.Core.Printing;
using PrintServer.Core.Queue;
using PrintServer.Queue;

namespace PrintServer.UnitTests;

public sealed class JobRecoveryTests : IDisposable
{
    private readonly QueueHarness _h = new();

    public void Dispose() => _h.Dispose();

    private async Task<Job> QueuedAsync(string name = "a.pdf", string? printer = "Office")
    {
        var result = await _h.IntakeAsync(_h.Drop(printer, name, $"content of {name}"));
        return await _h.GetAsync(result.JobId!.Value);
    }

    private Task<RecoveryReport> RecoverAsync() => _h.Recovery.RecoverAsync(CancellationToken.None);

    [Fact]
    public async Task AnEmptyQueueRecoversNothing()
    {
        Assert.Equal(new RecoveryReport(0, 0, 0, 0, 0, 0), await RecoverAsync());
    }

    [Fact]
    public async Task APendingJobWithItsFileIsLeftAlone()
    {
        var job = await QueuedAsync();

        Assert.Equal(new RecoveryReport(0, 0, 0, 0, 0, 0), await RecoverAsync());
        Assert.Equal(JobState.Pending, (await _h.GetAsync(job.Id)).State);
    }

    [Fact]
    public async Task AConvertingJobGoesBackToPending()
    {
        var job = await QueuedAsync();
        _h.Queue.SetState(job.Id, JobState.Converting);

        var report = await RecoverAsync();

        Assert.Equal(1, report.Requeued);
        Assert.Equal(JobState.Pending, (await _h.GetAsync(job.Id)).State);
    }

    [Fact]
    public async Task APrintingJobNeverAcknowledgedByTheBackendIsRequeued()
    {
        var job = await QueuedAsync();
        await _h.StartPrintingAsync(job.Id);

        var report = await RecoverAsync();

        Assert.Equal(1, report.Requeued);
        Assert.Equal(JobState.Pending, (await _h.GetAsync(job.Id)).State);
    }

    [Fact]
    public async Task AJobTheBackendCompletedIsClosedAndItsFileMovedToProcessed()
    {
        var job = await QueuedAsync();
        await _h.StartPrintingAsync(job.Id);
        await _h.Queue.Store.SetBackendJobIdAsync(job.Id, "9", CancellationToken.None);
        _h.Backend.Statuses["9"] = JobStatus.Completed;

        var report = await RecoverAsync();

        Assert.Equal(1, report.Completed);
        var done = await _h.GetAsync(job.Id);
        Assert.Equal(JobState.Completed, done.State);
        Assert.Equal(Path.Combine(_h.Folder("Office", "processati"), "a.pdf"), done.DocumentPath);
        Assert.True(File.Exists(done.DocumentPath));
    }

    [Fact]
    public async Task AJobTheBackendReportsInErrorFailsAndItsFileGoesToErrors()
    {
        var job = await QueuedAsync();
        await _h.StartPrintingAsync(job.Id);
        await _h.Queue.Store.SetBackendJobIdAsync(job.Id, "9", CancellationToken.None);
        _h.Backend.Statuses["9"] = JobStatus.Error;

        var report = await RecoverAsync();

        Assert.Equal(1, report.Failed);
        var failed = await _h.GetAsync(job.Id);
        Assert.Equal(JobState.Failed, failed.State);
        Assert.Equal(Path.Combine(_h.Folder("Office", "errori"), "a.pdf"), failed.DocumentPath);
    }

    [Theory]
    [InlineData(JobStatus.Printing)]
    [InlineData(JobStatus.Queued)]
    public async Task AJobStillInProgressAtTheBackendIsLeftPrinting(JobStatus status)
    {
        var job = await QueuedAsync();
        await _h.StartPrintingAsync(job.Id);
        await _h.Queue.Store.SetBackendJobIdAsync(job.Id, "9", CancellationToken.None);
        _h.Backend.Statuses["9"] = status;

        var report = await RecoverAsync();

        Assert.Equal(1, report.Resumed);
        Assert.Equal(JobState.Printing, (await _h.GetAsync(job.Id)).State);
    }

    [Fact]
    public async Task ABackendThatCannotBeAskedLeavesTheJobPrintingInsteadOfRiskingADoublePrint()
    {
        var job = await QueuedAsync();
        await _h.StartPrintingAsync(job.Id);
        await _h.Queue.Store.SetBackendJobIdAsync(job.Id, "9", CancellationToken.None);
        _h.Backend.ThrowOnStatus = new InvalidOperationException("cups down");

        var report = await RecoverAsync();

        Assert.Equal(1, report.Resumed);
        Assert.Equal(JobState.Printing, (await _h.GetAsync(job.Id)).State);
    }

    [Fact]
    public async Task AnInterruptedMoveIsCompletedFromTheSourceFile()
    {
        var file = _h.Drop("Office", "a.pdf", "hello");
        var jobId = Guid.NewGuid();
        var documentPath = Path.Combine(_h.Folder("Office"), "processing", jobId.ToString("N"), "a.pdf");
        await _h.Queue.Store.TryEnqueueAsync(
            new NewJob(jobId, _h.Folder("Office"), "a.pdf", file.FilePath, documentPath, "Office", file.CreatedUtc, "h", file.Length, DocumentFormats.Pdf, PrintOptions.Default),
            CancellationToken.None);

        var report = await RecoverAsync();

        Assert.Equal(1, report.Reconciled);
        Assert.True(File.Exists(documentPath));
        Assert.False(File.Exists(file.FilePath));
        Assert.Equal(JobState.Pending, (await _h.GetAsync(jobId)).State);
    }

    [Fact]
    public async Task AJobWhoseFileIsGoneFails()
    {
        var job = await QueuedAsync();
        File.Delete(job.DocumentPath);

        var report = await RecoverAsync();

        Assert.Equal(1, report.Lost);
        var failed = await _h.GetAsync(job.Id);
        Assert.Equal(JobState.Failed, failed.State);
        Assert.Equal("The file of the job is missing.", failed.LastError);
    }

    [Fact]
    public async Task AnInterruptedMoveThatStillFailsLosesTheJob()
    {
        var file = _h.Drop("Office", "a.pdf", "hello");
        var jobId = Guid.NewGuid();
        await _h.Queue.Store.TryEnqueueAsync(
            new NewJob(jobId, _h.Folder("Office"), "a.pdf", file.FilePath, Path.Combine(_h.Folder("Office"), "processing", jobId.ToString("N"), "a.pdf"), "Office", file.CreatedUtc, "h", file.Length, DocumentFormats.Pdf, PrintOptions.Default),
            CancellationToken.None);
        _h.Files.FailMove = true;

        var report = await RecoverAsync();

        Assert.Equal(1, report.Lost);
        Assert.Equal(JobState.Failed, (await _h.GetAsync(jobId)).State);
    }

    [Fact]
    public void ArgumentsAreValidated()
    {
        var logger = NullLogger<JobRecovery>.Instance;

        Assert.Throws<ArgumentNullException>(() => new JobRecovery(null!, _h.Queue.Store, _h.Files, logger));
        Assert.Throws<ArgumentNullException>(() => new JobRecovery(_h.Backend, null!, _h.Files, logger));
        Assert.Throws<ArgumentNullException>(() => new JobRecovery(_h.Backend, _h.Queue.Store, null!, logger));
        Assert.Throws<ArgumentNullException>(() => new JobRecovery(_h.Backend, _h.Queue.Store, _h.Files, null!));
    }
}

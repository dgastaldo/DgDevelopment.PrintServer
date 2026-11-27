using PrintServer.Core.Printing;
using PrintServer.Core.Queue;
using PrintServer.Queue;
using Microsoft.Extensions.Logging.Abstractions;

namespace PrintServer.UnitTests;

public sealed class QueueAdminTests : IDisposable
{
    private readonly QueueHarness _h = new();
    private readonly JobAdministrator _admin;

    public QueueAdminTests() =>
        _admin = new JobAdministrator(_h.Queue.Store, _h.Files, _h.Backend, NullLogger<JobAdministrator>.Instance);

    public void Dispose() => _h.Dispose();

    private async Task<Job> QueuedAsync(string name = "a.pdf", string? printer = "Office")
    {
        var result = await _h.IntakeAsync(_h.Drop(printer, name, $"content of {name}"));
        return await _h.GetAsync(result.JobId!.Value);
    }

    private Task RunAsync() => _h.Processor.RunOnceAsync(CancellationToken.None);

    private bool InFolder(string folder, string name) =>
        Directory.GetFiles(_h.Folder("Office", folder), name, SearchOption.AllDirectories).Length == 1;

    private async Task<Job> FailedAsync(string name = "a.pdf")
    {
        _h.Backend.Printers.Clear();
        _h.Backend.Printers.Add(new PrinterInfo("Other", IsDefault: true, PrinterAvailability.Ready));
        var job = await QueuedAsync(name);
        await RunAsync();
        _h.Backend.Printers.Clear();
        _h.Backend.Printers.Add(new PrinterInfo("Office", IsDefault: true, PrinterAvailability.Ready));
        var failed = await _h.GetAsync(job.Id);
        Assert.Equal(JobState.Failed, failed.State);
        return failed;
    }

    [Fact]
    public async Task AFailedJobCanBeRetriedAndPrintsAgain()
    {
        var failed = await FailedAsync();
        Assert.True(InFolder("errors", "a.pdf"));

        var result = await _admin.RetryAsync(failed.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        var retried = await _h.GetAsync(failed.Id);
        Assert.Equal(JobState.Pending, retried.State);
        Assert.Equal(0, retried.Attempts);
        Assert.Null(retried.LastError);
        Assert.Null(retried.FinishedAt);
        Assert.Equal(_h.Now, retried.CreatedAt);
        Assert.True(File.Exists(retried.DocumentPath));
        Assert.Contains(Path.Combine("processing", failed.Id.ToString("N")), retried.DocumentPath, StringComparison.Ordinal);
        Assert.False(InFolder("errors", "a.pdf"));

        await RunAsync();
        Assert.Equal(JobState.Printing, (await _h.GetAsync(failed.Id)).State);
    }

    [Fact]
    public async Task AConvertedJobIsConvertedAgainAfterARetry()
    {
        _h.Converter.FailWith = "corrupt";
        var job = await QueuedAsync("report.docx");
        await RunAsync();
        _h.Converter.FailWith = null;

        await _admin.RetryAsync(job.Id, CancellationToken.None);
        await RunAsync();

        Assert.Equal(2, _h.Converter.Calls.Count);
        Assert.Equal(JobState.Printing, (await _h.GetAsync(job.Id)).State);
    }

    [Theory]
    [InlineData(JobState.Pending)]
    [InlineData(JobState.Converting)]
    [InlineData(JobState.Printing)]
    [InlineData(JobState.Completed)]
    public async Task OnlyFailedStalledOrCancelledJobsCanBeRetried(JobState state)
    {
        var job = await QueuedAsync();
        _h.Queue.SetState(job.Id, state);

        var result = await _admin.RetryAsync(job.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("Only failed", result.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(JobState.Stalled)]
    [InlineData(JobState.Cancelled)]
    public async Task StalledAndCancelledJobsCanBeRetried(JobState state)
    {
        var job = await QueuedAsync();
        _h.Queue.SetState(job.Id, state);

        Assert.True((await _admin.RetryAsync(job.Id, CancellationToken.None)).Succeeded);
        Assert.Equal(JobState.Pending, (await _h.GetAsync(job.Id)).State);
    }

    [Fact]
    public async Task RetryingAnUnknownJobFails()
    {
        var result = await _admin.RetryAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("does not exist", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetryingAJobWhoseFileIsGoneFails()
    {
        var failed = await FailedAsync();
        File.Delete(failed.DocumentPath);

        var result = await _admin.RetryAsync(failed.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("gone", result.Message, StringComparison.Ordinal);
        Assert.Equal(JobState.Failed, (await _h.GetAsync(failed.Id)).State);
    }

    [Fact]
    public async Task RetryingWhileTheSameFileIsActiveIsAConflict()
    {
        var failed = await FailedAsync();
        await QueuedAsync("a.pdf");

        var result = await _admin.RetryAsync(failed.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("already in the queue", result.Message, StringComparison.Ordinal);
        Assert.Equal(JobState.Failed, (await _h.GetAsync(failed.Id)).State);
    }

    [Fact]
    public async Task ARetryThatCannotMoveTheFileFailsTheJobAgain()
    {
        var failed = await FailedAsync();
        _h.Files.FailMove = true;

        var result = await _admin.RetryAsync(failed.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("Could not move", result.Message, StringComparison.Ordinal);
        Assert.Equal(JobState.Failed, (await _h.GetAsync(failed.Id)).State);
    }

    [Fact]
    public async Task AJobWhoseFileNeverLeftProcessingIsRetriedInPlace()
    {
        var job = await QueuedAsync();
        _h.Queue.SetState(job.Id, JobState.Failed);

        Assert.True((await _admin.RetryAsync(job.Id, CancellationToken.None)).Succeeded);
        Assert.True(File.Exists(job.DocumentPath));
    }

    [Fact]
    public async Task APendingJobCanBeCancelled()
    {
        var job = await QueuedAsync();

        var result = await _admin.CancelAsync(job.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Null(result.Message);
        var cancelled = await _h.GetAsync(job.Id);
        Assert.Equal(JobState.Cancelled, cancelled.State);
        Assert.Equal("Cancelled by the user.", cancelled.LastError);
        Assert.NotNull(cancelled.FinishedAt);
        Assert.True(InFolder("cancelled", "a.pdf"));
        await RunAsync();
        Assert.Empty(_h.Backend.Requests);
    }

    [Fact]
    public async Task APrintingJobIsCancelledOnThePrinterToo()
    {
        var job = await QueuedAsync();
        await RunAsync();

        var result = await _admin.CancelAsync(job.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Null(result.Message);
        Assert.Equal(["1"], _h.Backend.Cancelled);
        Assert.Equal(JobState.Cancelled, (await _h.GetAsync(job.Id)).State);
        Assert.True(InFolder("cancelled", "a.pdf"));
        _h.Backend.Statuses["1"] = JobStatus.Completed;
        await RunAsync();
        Assert.Equal(JobState.Cancelled, (await _h.GetAsync(job.Id)).State);
    }

    [Fact]
    public async Task APrinterThatCannotCancelLeavesAWarningButTheJobIsCancelled()
    {
        var job = await QueuedAsync();
        await RunAsync();
        _h.Backend.ThrowOnCancel = new HttpRequestException("down");

        var result = await _admin.CancelAsync(job.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("down", result.Message, StringComparison.Ordinal);
        Assert.Equal(JobState.Cancelled, (await _h.GetAsync(job.Id)).State);
    }

    [Fact]
    public async Task APrinterThatDoesNotConfirmLeavesAWarning()
    {
        var job = await QueuedAsync();
        await RunAsync();
        _h.Backend.CancelResult = false;

        var result = await _admin.CancelAsync(job.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("did not confirm", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APrintingJobNotYetKnownToThePrinterIsCancelledWithoutAskingIt()
    {
        var job = await QueuedAsync();
        await _h.StartPrintingAsync(job.Id);

        var result = await _admin.CancelAsync(job.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(_h.Backend.Cancelled);
    }

    [Fact]
    public async Task AConvertingJobIsCancelledAndItsFileIsLeftToTheConversion()
    {
        var job = await QueuedAsync("report.docx");
        _h.Queue.SetState(job.Id, JobState.Converting);

        var result = await _admin.CancelAsync(job.Id, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(JobState.Cancelled, (await _h.GetAsync(job.Id)).State);
        Assert.True(File.Exists(job.DocumentPath));
    }

    [Theory]
    [InlineData(JobState.Completed)]
    [InlineData(JobState.Failed)]
    [InlineData(JobState.Stalled)]
    [InlineData(JobState.Cancelled)]
    public async Task AFinishedJobCannotBeCancelled(JobState state)
    {
        var job = await QueuedAsync();
        _h.Queue.SetState(job.Id, state);

        var result = await _admin.CancelAsync(job.Id, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("already finished", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellingAnUnknownJobFails()
    {
        Assert.False((await _admin.CancelAsync(Guid.NewGuid(), CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task ACancelledJobWhoseFileCannotBeMovedIsStillCancelled()
    {
        var job = await QueuedAsync();
        _h.Files.FailMove = true;

        Assert.True((await _admin.CancelAsync(job.Id, CancellationToken.None)).Succeeded);
        Assert.Equal(JobState.Cancelled, (await _h.GetAsync(job.Id)).State);
    }

    [Fact]
    public async Task AJobCancelledWhileBeingSentIsDroppedFromThePrinter()
    {
        var job = await QueuedAsync();
        _h.Backend.OnPrint = () => _admin.CancelAsync(job.Id, CancellationToken.None).GetAwaiter().GetResult();

        await RunAsync();

        Assert.Equal(JobState.Cancelled, (await _h.GetAsync(job.Id)).State);
        Assert.Equal(["1"], _h.Backend.Cancelled);
    }

    [Fact]
    public async Task AJobCancelledWhileBeingSentIsStillReportedWhenThePrinterRefusesToDropIt()
    {
        var job = await QueuedAsync();
        _h.Backend.ThrowOnCancel = new TimeoutException("slow");
        _h.Backend.OnPrint = () => _admin.CancelAsync(job.Id, CancellationToken.None).GetAwaiter().GetResult();

        await RunAsync();

        Assert.Equal(JobState.Cancelled, (await _h.GetAsync(job.Id)).State);
    }

    [Fact]
    public async Task AJobCancelledWhileBeingConvertedIsNotPrintedAndItsFilesAreCleaned()
    {
        var job = await QueuedAsync("report.docx");
        _h.Converter.OnConvert = () => _admin.CancelAsync(job.Id, CancellationToken.None).GetAwaiter().GetResult();

        await RunAsync();

        Assert.Equal(JobState.Cancelled, (await _h.GetAsync(job.Id)).State);
        Assert.Empty(_h.Backend.Requests);
        Assert.True(InFolder("cancelled", "report.docx"));
        Assert.Empty(Directory.GetFiles(_h.Inbox, "*.pdf", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task TheQueueCanBeQueriedPagedFilteredAndCounted()
    {
        for (var index = 1; index <= 5; index++)
        {
            await QueuedAsync($"file{index}.pdf");
            _h.Queue.Time.Advance(TimeSpan.FromSeconds(1));
        }

        await QueuedAsync("100%_done.pdf");
        var all = await _h.Queue.Store.QueryAsync(new JobQuery(), 0, 4, CancellationToken.None);
        var second = await _h.Queue.Store.QueryAsync(new JobQuery(), 4, 4, CancellationToken.None);
        var search = await _h.Queue.Store.QueryAsync(new JobQuery(Search: "file3"), 0, 10, CancellationToken.None);
        var escaped = await _h.Queue.Store.QueryAsync(new JobQuery(Search: "100%_"), 0, 10, CancellationToken.None);
        var literal = await _h.Queue.Store.QueryAsync(new JobQuery(Search: "%"), 0, 10, CancellationToken.None);
        var none = await _h.Queue.Store.QueryAsync(new JobQuery(JobState.Failed), 0, 10, CancellationToken.None);

        Assert.Equal(6, all.Total);
        Assert.Equal(4, all.Items.Count);
        Assert.Equal("100%_done.pdf", all.Items[0].FileName);
        Assert.Equal(2, second.Items.Count);
        Assert.Equal("file3.pdf", Assert.Single(search.Items).FileName);
        Assert.Equal("100%_done.pdf", Assert.Single(escaped.Items).FileName);
        Assert.Single(literal.Items);
        Assert.Equal(0, none.Total);
        var counts = await _h.Queue.Store.CountByStateAsync(CancellationToken.None);
        Assert.Equal(6, counts[JobState.Pending]);
        Assert.False(counts.ContainsKey(JobState.Failed));
    }

    [Fact]
    public async Task TheQueryChecksItsArguments()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _h.Queue.Store.QueryAsync(null!, 0, 10, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _h.Queue.Store.QueryAsync(new JobQuery(), -1, 10, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _h.Queue.Store.QueryAsync(new JobQuery(), 0, 0, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => _h.Queue.Store.CancelAsync(Guid.NewGuid(), " ", CancellationToken.None));
    }

    [Fact]
    public async Task ResultsKnowTheirOutcome()
    {
        Assert.True(QueueActionResult.Success().Succeeded);
        Assert.Equal("careful", QueueActionResult.Success("careful").Message);
        Assert.False(QueueActionResult.Failure("no").Succeeded);
        Assert.Throws<ArgumentException>(() => QueueActionResult.Failure(" "));
        Assert.Equal(RestartOutcome.NotRestartable, await _h.Queue.Store.RestartAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public void TheAdministratorChecksItsArguments()
    {
        var logger = NullLogger<JobAdministrator>.Instance;

        Assert.Throws<ArgumentNullException>(() => new JobAdministrator(null!, _h.Files, _h.Backend, logger));
        Assert.Throws<ArgumentNullException>(() => new JobAdministrator(_h.Queue.Store, null!, _h.Backend, logger));
        Assert.Throws<ArgumentNullException>(() => new JobAdministrator(_h.Queue.Store, _h.Files, null!, logger));
        Assert.Throws<ArgumentNullException>(() => new JobAdministrator(_h.Queue.Store, _h.Files, _h.Backend, null!));
    }
}

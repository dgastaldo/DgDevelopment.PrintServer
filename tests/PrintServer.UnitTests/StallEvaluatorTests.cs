using Microsoft.Extensions.Logging.Abstractions;
using PrintServer.Core.Printing;
using PrintServer.Core.Queue;
using PrintServer.Queue;

namespace PrintServer.UnitTests;

public sealed class StallEvaluatorTests : IDisposable
{
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    private readonly QueueHarness _h = new();

    public void Dispose() => _h.Dispose();

    private async Task<Job> PrintingJobAsync(string? printer = "Office")
    {
        var result = await _h.IntakeAsync(_h.Drop(printer, "a.pdf", "hello"));
        return await _h.StartPrintingAsync(result.JobId!.Value);
    }

    private Task<StallVerdict> EvaluateAsync(Job job) => _h.Stalls.EvaluateAsync(job, CancellationToken.None);

    [Fact]
    public async Task AJobThatIsNotPrintingIsNeverJudged()
    {
        var result = await _h.IntakeAsync(_h.Drop("Office", "a.pdf", "hello"));
        var pending = await _h.GetAsync(result.JobId!.Value);
        _h.Queue.Time.Advance(TimeSpan.FromHours(5));

        Assert.Equal(StallVerdict.NotStalled, await EvaluateAsync(pending));
    }

    [Fact]
    public async Task AYoungPrintingJobIsLeftAlone()
    {
        var job = await PrintingJobAsync();
        _h.Queue.Time.Advance(Hour - TimeSpan.FromSeconds(1));

        Assert.Equal(StallVerdict.NotStalled, await EvaluateAsync(job));
        Assert.Equal(JobState.Printing, (await _h.GetAsync(job.Id)).State);
    }

    [Fact]
    public async Task AnHourWithAHealthyPrinterStallsTheJobAndMovesItsFile()
    {
        var job = await PrintingJobAsync();
        _h.Queue.Time.Advance(Hour);

        Assert.Equal(StallVerdict.Stalled, await EvaluateAsync(job));

        var stalled = await _h.GetAsync(job.Id);
        Assert.Equal(JobState.Stalled, stalled.State);
        Assert.StartsWith("Stalled: no progress for 01:00:00", stalled.LastError, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(_h.Folder("Office", "stalled"), "a.pdf"), stalled.DocumentPath);
        Assert.True(File.Exists(stalled.DocumentPath));
    }

    [Theory]
    [InlineData(PrinterAvailability.Offline, PrinterIssues.None)]
    [InlineData(PrinterAvailability.Ready, PrinterIssues.PaperOut)]
    [InlineData(PrinterAvailability.Busy, PrinterIssues.TonerOut)]
    [InlineData(PrinterAvailability.Ready, PrinterIssues.PaperJam)]
    [InlineData(PrinterAvailability.Ready, PrinterIssues.DoorOpen)]
    [InlineData(PrinterAvailability.Ready, PrinterIssues.Error)]
    public async Task APrinterReportingAProblemKeepsTheJobWaitingAndRestartsTheClock(PrinterAvailability availability, PrinterIssues issues)
    {
        var job = await PrintingJobAsync();
        _h.Backend.Printers[0] = new PrinterInfo("Office", IsDefault: true, availability, issues);
        _h.Queue.Time.Advance(TimeSpan.FromHours(2));

        Assert.Equal(StallVerdict.NotStalled, await EvaluateAsync(job));

        var waiting = await _h.GetAsync(job.Id);
        Assert.Equal(JobState.Printing, waiting.State);
        Assert.Equal(_h.Now, waiting.LastBlockedAt);
    }

    [Fact]
    public async Task OnceThePrinterRecoversTheJobStallsOnlyAfterAFullHealthyHour()
    {
        var job = await PrintingJobAsync();
        _h.Backend.Printers[0] = new PrinterInfo("Office", IsDefault: true, PrinterAvailability.Ready, PrinterIssues.PaperOut);
        _h.Queue.Time.Advance(TimeSpan.FromHours(2));
        await EvaluateAsync(job);
        _h.Backend.Printers[0] = new PrinterInfo("Office", IsDefault: true, PrinterAvailability.Ready);
        var afterBlock = await _h.GetAsync(job.Id);

        _h.Queue.Time.Advance(TimeSpan.FromMinutes(59));
        Assert.Equal(StallVerdict.NotStalled, await EvaluateAsync(afterBlock));

        _h.Queue.Time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(StallVerdict.Stalled, await EvaluateAsync(afterBlock));
    }

    [Fact]
    public async Task APrinterThatIsNotListedCountsAsUnreachable()
    {
        var job = await PrintingJobAsync();
        _h.Backend.Printers.Clear();
        _h.Queue.Time.Advance(Hour);

        Assert.Equal(StallVerdict.NotStalled, await EvaluateAsync(job));
    }

    [Fact]
    public async Task ABackendThatCannotBeReachedKeepsTheJobWaiting()
    {
        var job = await PrintingJobAsync();
        _h.Backend.ThrowOnList = new InvalidOperationException("cups down");
        _h.Queue.Time.Advance(Hour);

        Assert.Equal(StallVerdict.NotStalled, await EvaluateAsync(job));
    }

    [Fact]
    public async Task AJobWithoutAPrinterIsJudgedAgainstTheDefaultOne()
    {
        var job = await PrintingJobAsync(printer: null);
        _h.Backend.Printers.Clear();
        _h.Backend.Printers.Add(new PrinterInfo("Other", IsDefault: false, PrinterAvailability.Offline));
        _h.Backend.Printers.Add(new PrinterInfo("Main", IsDefault: true, PrinterAvailability.Ready));
        _h.Queue.Time.Advance(Hour);

        Assert.Equal(StallVerdict.Stalled, await EvaluateAsync(job));
    }

    [Fact]
    public async Task ThePrinterNameIsMatchedIgnoringCase()
    {
        var job = await PrintingJobAsync();
        _h.Backend.Printers[0] = new PrinterInfo("OFFICE", IsDefault: false, PrinterAvailability.Offline);
        _h.Queue.Time.Advance(Hour);

        Assert.Equal(StallVerdict.NotStalled, await EvaluateAsync(job));
    }

    [Fact]
    public async Task ACompletedJobIsClosedWhenTheBackendSaysSo()
    {
        var job = await PrintingJobAsync();
        await _h.Queue.Store.SetBackendJobIdAsync(job.Id, "77", CancellationToken.None);
        _h.Backend.Statuses["77"] = JobStatus.Completed;
        _h.Queue.Time.Advance(Hour);

        Assert.Equal(StallVerdict.Completed, await EvaluateAsync(await _h.GetAsync(job.Id)));

        var done = await _h.GetAsync(job.Id);
        Assert.Equal(JobState.Completed, done.State);
        Assert.Equal(Path.Combine(_h.Folder("Office", "processed"), "a.pdf"), done.DocumentPath);
    }

    [Fact]
    public async Task AJobTheBackendReportsInErrorFails()
    {
        var job = await PrintingJobAsync();
        await _h.Queue.Store.SetBackendJobIdAsync(job.Id, "77", CancellationToken.None);
        _h.Backend.Statuses["77"] = JobStatus.Error;
        _h.Queue.Time.Advance(Hour);

        Assert.Equal(StallVerdict.Failed, await EvaluateAsync(await _h.GetAsync(job.Id)));

        var failed = await _h.GetAsync(job.Id);
        Assert.Equal(JobState.Failed, failed.State);
        Assert.Equal(Path.Combine(_h.Folder("Office", "errors"), "a.pdf"), failed.DocumentPath);
    }

    [Theory]
    [InlineData(JobStatus.Printing)]
    [InlineData(JobStatus.Queued)]
    public async Task AJobStillShownAsInProgressByTheBackendStallsAfterTheHour(JobStatus reported)
    {
        var job = await PrintingJobAsync();
        await _h.Queue.Store.SetBackendJobIdAsync(job.Id, "77", CancellationToken.None);
        _h.Backend.Statuses["77"] = reported;
        _h.Queue.Time.Advance(Hour);

        Assert.Equal(StallVerdict.Stalled, await EvaluateAsync(await _h.GetAsync(job.Id)));
    }

    [Fact]
    public async Task AStatusQueryThatFailsDoesNotPreventTheStall()
    {
        var job = await PrintingJobAsync();
        await _h.Queue.Store.SetBackendJobIdAsync(job.Id, "77", CancellationToken.None);
        _h.Backend.ThrowOnStatus = new IOException("timeout");
        _h.Queue.Time.Advance(Hour);

        Assert.Equal(StallVerdict.Stalled, await EvaluateAsync(await _h.GetAsync(job.Id)));
    }

    [Fact]
    public async Task AJobThatChangedStateInTheMeantimeIsNotOverwritten()
    {
        var job = await PrintingJobAsync();
        _h.Queue.Time.Advance(Hour);
        await _h.Queue.Store.CompleteAsync(job.Id, CancellationToken.None);

        Assert.Equal(StallVerdict.NotStalled, await EvaluateAsync(job));
        Assert.Equal(JobState.Completed, (await _h.GetAsync(job.Id)).State);
    }

    [Fact]
    public async Task AStalledJobWhoseFileCannotBeMovedIsStillStalled()
    {
        var job = await PrintingJobAsync();
        _h.Files.FailMove = true;
        _h.Queue.Time.Advance(Hour);

        Assert.Equal(StallVerdict.Stalled, await EvaluateAsync(job));
        Assert.Equal(JobState.Stalled, (await _h.GetAsync(job.Id)).State);
    }

    [Fact]
    public async Task ArgumentsAreValidated()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _h.Stalls.EvaluateAsync(null!, CancellationToken.None));

        var options = Microsoft.Extensions.Options.Options.Create(_h.Options);
        var logger = NullLogger<StallEvaluator>.Instance;
        Assert.Throws<ArgumentNullException>(() => new StallEvaluator(null!, _h.Queue.Store, _h.Files, options, _h.Queue.Time, logger));
        Assert.Throws<ArgumentNullException>(() => new StallEvaluator(_h.Backend, null!, _h.Files, options, _h.Queue.Time, logger));
        Assert.Throws<ArgumentNullException>(() => new StallEvaluator(_h.Backend, _h.Queue.Store, null!, options, _h.Queue.Time, logger));
        Assert.Throws<ArgumentNullException>(() => new StallEvaluator(_h.Backend, _h.Queue.Store, _h.Files, null!, _h.Queue.Time, logger));
        Assert.Throws<ArgumentNullException>(() => new StallEvaluator(_h.Backend, _h.Queue.Store, _h.Files, options, null!, logger));
        Assert.Throws<ArgumentNullException>(() => new StallEvaluator(_h.Backend, _h.Queue.Store, _h.Files, options, _h.Queue.Time, null!));
    }
}

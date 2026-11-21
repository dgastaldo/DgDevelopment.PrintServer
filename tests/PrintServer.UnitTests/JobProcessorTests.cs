using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PrintServer.Core.Printing;
using PrintServer.Core.Queue;
using PrintServer.Queue;

namespace PrintServer.UnitTests;

public sealed class JobProcessorTests : IDisposable
{
    private readonly QueueHarness _h = new();

    public void Dispose() => _h.Dispose();

    private async Task<Job> QueuedAsync(string name = "a.pdf", string? printer = "Office")
    {
        var result = await _h.IntakeAsync(_h.Drop(printer, name, $"content of {name}"));
        return await _h.GetAsync(result.JobId!.Value);
    }

    private Task RunAsync() => _h.Processor.RunOnceAsync(CancellationToken.None);

    private bool InFolder(string? printer, string folder, string name) =>
        Directory.GetFiles(_h.Folder(printer, folder), name, SearchOption.AllDirectories).Length == 1;

    [Fact]
    public async Task AnEmptyQueueDoesNothing()
    {
        await RunAsync();

        Assert.Empty(_h.Backend.Requests);
    }

    [Fact]
    public async Task APendingJobIsSentToItsPrinterWithItsOptions()
    {
        var job = await QueuedAsync();

        await RunAsync();

        var request = Assert.Single(_h.Backend.Requests);
        Assert.Equal(job.DocumentPath, request.FilePath);
        Assert.Equal("Office", request.PrinterName);
        Assert.Equal(1, request.Copies);
        Assert.Equal("a.pdf", request.JobName);
        var sent = await _h.GetAsync(job.Id);
        Assert.Equal(JobState.Printing, sent.State);
        Assert.Equal("1", sent.BackendJobId);
        Assert.Equal(1, sent.Attempts);
    }

    [Fact]
    public async Task AJobWithoutAPrinterUsesTheDefaultOne()
    {
        _h.Backend.Printers.Clear();
        _h.Backend.Printers.Add(new PrinterInfo("Other", IsDefault: false, PrinterAvailability.Ready));
        _h.Backend.Printers.Add(new PrinterInfo("Main", IsDefault: true, PrinterAvailability.Ready));
        await QueuedAsync(printer: null);

        await RunAsync();

        Assert.Equal("Main", Assert.Single(_h.Backend.Requests).PrinterName);
    }

    [Fact]
    public async Task ThePrinterNameIsMatchedWithoutCase()
    {
        await QueuedAsync(printer: "office");

        await RunAsync();

        Assert.Equal("Office", Assert.Single(_h.Backend.Requests).PrinterName);
    }

    [Fact]
    public async Task AJobForAnUnknownPrinterFailsAtOnceAndMovesToErrors()
    {
        var job = await QueuedAsync(printer: "Nowhere");

        await RunAsync();

        var failed = await _h.GetAsync(job.Id);
        Assert.Equal(JobState.Failed, failed.State);
        Assert.Contains("Nowhere", failed.LastError, StringComparison.Ordinal);
        Assert.Empty(_h.Backend.Requests);
        Assert.True(InFolder("Nowhere", "errori", "a.pdf"));
    }

    [Fact]
    public async Task AJobWithoutAPrinterFailsWhenThereIsNoDefault()
    {
        _h.Backend.Printers.Clear();
        var job = await QueuedAsync(printer: null);

        await RunAsync();

        var failed = await _h.GetAsync(job.Id);
        Assert.Equal(JobState.Failed, failed.State);
        Assert.Contains("default", failed.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AJobWhoseFileVanishedFailsWithoutCallingTheBackend()
    {
        var job = await QueuedAsync();
        File.Delete(job.DocumentPath);

        await RunAsync();

        var failed = await _h.GetAsync(job.Id);
        Assert.Equal(JobState.Failed, failed.State);
        Assert.Equal("The file of the job is missing.", failed.LastError);
        Assert.Empty(_h.Backend.Requests);
    }

    [Fact]
    public async Task OnlyOneJobPerPrinterIsSentAtATime()
    {
        await QueuedAsync("a.pdf");
        await QueuedAsync("b.pdf");

        await RunAsync();

        Assert.Equal("a.pdf", Assert.Single(_h.Backend.Requests).JobName);
    }

    [Fact]
    public async Task DifferentPrintersPrintInParallel()
    {
        _h.Backend.Printers.Add(new PrinterInfo("Labels", IsDefault: false, PrinterAvailability.Ready));
        await QueuedAsync("a.pdf", "Office");
        await QueuedAsync("b.pdf", "Labels");

        await RunAsync();

        Assert.Equal(["Labels", "Office"], _h.Backend.Requests.Select(r => r.PrinterName).Order());
    }

    [Fact]
    public async Task ACompletedJobMovesToProcessedAndFreesThePrinter()
    {
        var first = await QueuedAsync("a.pdf");
        await QueuedAsync("b.pdf");
        await RunAsync();
        _h.Backend.Statuses["1"] = JobStatus.Completed;

        await RunAsync();

        var done = await _h.GetAsync(first.Id);
        Assert.Equal(JobState.Completed, done.State);
        Assert.True(InFolder("Office", "processati", "a.pdf"));
        Assert.Equal(["a.pdf", "b.pdf"], _h.Backend.Requests.Select(r => r.JobName));
    }

    [Fact]
    public async Task AJobStillPrintingIsLeftAlone()
    {
        var job = await QueuedAsync();
        await RunAsync();

        await RunAsync();

        Assert.Equal(JobState.Printing, (await _h.GetAsync(job.Id)).State);
        Assert.Single(_h.Backend.Requests);
    }

    [Fact]
    public async Task ARefusedJobIsRetriedWithExponentialDelays()
    {
        _h.Backend.RefuseWith = "printer busy";
        var job = await QueuedAsync();
        var delays = new List<TimeSpan>();

        for (var attempt = 1; attempt < _h.Options.MaxAttempts; attempt++)
        {
            await RunAsync();
            var retried = await _h.GetAsync(job.Id);
            Assert.Equal(JobState.Pending, retried.State);
            Assert.Equal(attempt, retried.Attempts);
            Assert.Equal("printer busy", retried.LastError);
            delays.Add(retried.NextAttemptAt!.Value - _h.Now);
            _h.Queue.Time.Advance(delays[^1]);
        }

        Assert.Equal(
            [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(4)],
            delays);
    }

    [Fact]
    public async Task ARetryIsNotSentBeforeItsTime()
    {
        _h.Backend.RefuseWith = "printer busy";
        await QueuedAsync();
        await RunAsync();

        _h.Queue.Time.Advance(TimeSpan.FromSeconds(29));
        await RunAsync();

        Assert.Single(_h.Backend.Requests);
    }

    [Fact]
    public async Task AJobFailsAfterTheLastAttemptAndMovesToErrors()
    {
        _h.Backend.RefuseWith = "printer busy";
        var job = await QueuedAsync();

        for (var attempt = 1; attempt <= _h.Options.MaxAttempts; attempt++)
        {
            await RunAsync();
            _h.Queue.Time.Advance(TimeSpan.FromHours(1));
        }

        var failed = await _h.GetAsync(job.Id);
        Assert.Equal(JobState.Failed, failed.State);
        Assert.Equal(5, failed.Attempts);
        Assert.Equal("printer busy (gave up after 5 attempts)", failed.LastError);
        Assert.Equal(5, _h.Backend.Requests.Count);
        Assert.True(InFolder("Office", "errori", "a.pdf"));
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(TimeoutException))]
    public async Task ABackendExceptionCountsAsAFailedAttempt(Type exceptionType)
    {
        _h.Backend.ThrowOnPrint = (Exception)Activator.CreateInstance(exceptionType, "boom")!;
        var job = await QueuedAsync();

        await RunAsync();

        var retried = await _h.GetAsync(job.Id);
        Assert.Equal(JobState.Pending, retried.State);
        Assert.Contains("boom", retried.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnreachableBackendWhenListingPrintersIsRetried()
    {
        _h.Backend.ThrowOnList = new HttpRequestException("down");
        var job = await QueuedAsync();

        await RunAsync();

        var retried = await _h.GetAsync(job.Id);
        Assert.Equal(JobState.Pending, retried.State);
        Assert.Contains("down", retried.LastError, StringComparison.Ordinal);
        Assert.Empty(_h.Backend.Requests);
    }

    [Fact]
    public async Task APrinterErrorOnAJobInPrintIsRetried()
    {
        var job = await QueuedAsync();
        await RunAsync();
        _h.Backend.Statuses["1"] = JobStatus.Error;

        await RunAsync();

        var retried = await _h.GetAsync(job.Id);
        Assert.Equal(JobState.Pending, retried.State);
        Assert.Equal("The printer reported an error for the job.", retried.LastError);
        Assert.Null(retried.BackendJobId);
    }

    [Fact]
    public async Task APrinterErrorOnTheLastAttemptFails()
    {
        var job = await QueuedAsync();
        _h.Queue.Time.Advance(TimeSpan.Zero);
        for (var attempt = 1; attempt <= _h.Options.MaxAttempts; attempt++)
        {
            await RunAsync();
            _h.Backend.Statuses[attempt.ToString(System.Globalization.CultureInfo.InvariantCulture)] = JobStatus.Error;
            await RunAsync();
            _h.Queue.Time.Advance(TimeSpan.FromHours(1));
        }

        var failed = await _h.GetAsync(job.Id);
        Assert.Equal(JobState.Failed, failed.State);
        Assert.True(InFolder("Office", "errori", "a.pdf"));
    }

    [Fact]
    public async Task AnUnavailableStatusLeavesTheJobPrinting()
    {
        var job = await QueuedAsync();
        await RunAsync();
        _h.Backend.ThrowOnStatus = new HttpRequestException("down");

        await RunAsync();

        Assert.Equal(JobState.Printing, (await _h.GetAsync(job.Id)).State);
    }

    [Fact]
    public async Task AJobPrintingForTooLongWithoutProblemsStalls()
    {
        var job = await QueuedAsync();
        await RunAsync();
        _h.Queue.Time.Advance(TimeSpan.FromHours(2));

        await RunAsync();

        Assert.Equal(JobState.Stalled, (await _h.GetAsync(job.Id)).State);
        Assert.True(InFolder("Office", "stallo", "a.pdf"));
    }

    [Fact]
    public async Task AJobWaitingOnABlockedPrinterDoesNotStall()
    {
        _h.Backend.Printers[0] = new PrinterInfo("Office", IsDefault: true, PrinterAvailability.Ready, PrinterIssues.PaperOut);
        var job = await QueuedAsync();
        await RunAsync();
        _h.Queue.Time.Advance(TimeSpan.FromHours(2));

        await RunAsync();

        var waiting = await _h.GetAsync(job.Id);
        Assert.Equal(JobState.Printing, waiting.State);
        Assert.NotNull(waiting.LastBlockedAt);
    }

    [Fact]
    public async Task TheServiceKeepsProcessingAfterAFailure()
    {
        await QueuedAsync();
        var options = Options.Create(new QueueOptions { PollInterval = TimeSpan.FromMilliseconds(20) });
        var failing = new JobProcessor(
            new ScriptedPrinterBackend(),
            new FailingFirstLookupJobStoreForProcessing(_h.Queue.Store),
            _h.Files,
            _h.Stalls,
            _h.Converter,
            new RetryPolicy(options, TimeProvider.System),
            NullLogger<JobProcessor>.Instance);
        using var service = new JobProcessingService(failing, options, TimeProvider.System, NullLogger<JobProcessingService>.Instance);
        await service.StartAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while ((await _h.Queue.Store.ListAsync(JobState.Printing, cts.Token)).Count == 0)
        {
            await Task.Delay(20, cts.Token);
        }

        await service.StopAsync(CancellationToken.None);

        Assert.Single(await _h.Queue.Store.ListAsync(JobState.Printing, CancellationToken.None));
    }

    [Fact]
    public void TheRetryPolicyDoublesTheDelayAndGivesUpAtTheLimit()
    {
        var policy = new RetryPolicy(Options.Create(new QueueOptions { MaxAttempts = 3, RetryBaseDelay = TimeSpan.FromSeconds(10) }), _h.Queue.Time);

        Assert.Equal(TimeSpan.FromSeconds(10), policy.DelayAfter(1));
        Assert.Equal(TimeSpan.FromSeconds(20), policy.DelayAfter(2));
        Assert.Equal(TimeSpan.FromSeconds(40), policy.DelayAfter(3));
        Assert.Equal(_h.Now + TimeSpan.FromSeconds(20), policy.NextAttemptAfter(2));
        Assert.False(policy.IsExhausted(2));
        Assert.True(policy.IsExhausted(3));
    }
}

internal sealed class FailingFirstLookupJobStoreForProcessing(IJobStore inner) : IJobStore
{
    private int _lists;

    public Task<bool> TryEnqueueAsync(NewJob job, CancellationToken ct) => inner.TryEnqueueAsync(job, ct);

    public Task<Job?> GetAsync(Guid id, CancellationToken ct) => inner.GetAsync(id, ct);

    public Task<IReadOnlyList<Job>> ListAsync(JobState state, CancellationToken ct) =>
        Interlocked.Increment(ref _lists) == 1
            ? throw new InvalidOperationException("database unavailable")
            : inner.ListAsync(state, ct);

    public Task<JobPage> QueryAsync(JobQuery query, int skip, int take, CancellationToken ct) =>
        inner.QueryAsync(query, skip, take, ct);

    public Task<IReadOnlyDictionary<JobState, int>> CountByStateAsync(CancellationToken ct) => inner.CountByStateAsync(ct);

    public Task<bool> CancelAsync(Guid id, string reason, CancellationToken ct) => inner.CancelAsync(id, reason, ct);

    public Task<RestartOutcome> RestartAsync(Guid id, CancellationToken ct) => inner.RestartAsync(id, ct);

    public Task<IReadOnlyList<Job>> FindActiveAsync(string originDirectory, string fileName, CancellationToken ct) =>
        inner.FindActiveAsync(originDirectory, fileName, ct);

    public Task<Job?> ClaimNextAsync(CancellationToken ct) => inner.ClaimNextAsync(ct);

    public Task<Job?> ClaimNextConversionAsync(CancellationToken ct) => inner.ClaimNextConversionAsync(ct);

    public Task<bool> FinishConversionAsync(Guid id, string documentFormat, CancellationToken ct) =>
        inner.FinishConversionAsync(id, documentFormat, ct);

    public Task<bool> SetBackendJobIdAsync(Guid id, string backendJobId, CancellationToken ct) =>
        inner.SetBackendJobIdAsync(id, backendJobId, ct);

    public Task<bool> MarkBlockedAsync(Guid id, CancellationToken ct) => inner.MarkBlockedAsync(id, ct);

    public Task<bool> CompleteAsync(Guid id, CancellationToken ct) => inner.CompleteAsync(id, ct);

    public Task<bool> FailAsync(Guid id, string reason, CancellationToken ct) => inner.FailAsync(id, reason, ct);

    public Task<bool> StallAsync(Guid id, string reason, CancellationToken ct) => inner.StallAsync(id, reason, ct);

    public Task<bool> RetryLaterAsync(Guid id, string reason, DateTimeOffset nextAttemptAt, CancellationToken ct) =>
        inner.RetryLaterAsync(id, reason, nextAttemptAt, ct);

    public Task<bool> RequeueAsync(Guid id, CancellationToken ct) => inner.RequeueAsync(id, ct);

    public Task<bool> UpdateDocumentPathAsync(Guid id, string documentPath, CancellationToken ct) =>
        inner.UpdateDocumentPathAsync(id, documentPath, ct);
}

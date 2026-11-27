using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PrintServer.Core.Printing;
using PrintServer.Core.Queue;
using PrintServer.Core.Watching;
using PrintServer.Queue;

namespace PrintServer.UnitTests;

public sealed class JobIntakeTests : IDisposable
{
    private static readonly DateTimeOffset Old = SqliteQueueFixture.Start.AddHours(-3);
    private static readonly DateTimeOffset Newer = SqliteQueueFixture.Start.AddHours(-1);

    private readonly QueueHarness _h = new();

    public void Dispose() => _h.Dispose();

    private static string Sha(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    [Fact]
    public async Task ANewFileIsQueuedAndMovedToItsProcessingFolder()
    {
        var file = _h.Drop("Office", "a.pdf", "hello", Old);

        var result = await _h.IntakeAsync(file);

        Assert.Equal(IntakeOutcome.Enqueued, result.Outcome);
        Assert.Empty(result.ReplacedJobIds);
        var job = await _h.GetAsync(result.JobId!.Value);
        Assert.Equal(JobState.Pending, job.State);
        Assert.Equal(_h.Folder("Office"), job.OriginDirectory);
        Assert.Equal("a.pdf", job.FileName);
        Assert.Equal("Office", job.PrinterName);
        Assert.Equal(Sha("hello"), job.FileHash);
        Assert.Equal(file.Length, job.FileSize);
        Assert.Equal(Old, job.FileCreatedUtc);
        Assert.Equal(DocumentFormats.Pdf, job.DocumentFormat);
        Assert.Equal(PrintOptions.Default, job.Options);
        Assert.Equal(Path.Combine(_h.Folder("Office"), "processing", job.Id.ToString("N"), "a.pdf"), job.DocumentPath);
        Assert.True(File.Exists(job.DocumentPath));
        Assert.False(File.Exists(file.FilePath));
    }

    [Fact]
    public async Task AFileInTheRootHasNoPrinter()
    {
        var result = await _h.IntakeAsync(_h.Drop(null, "a.pdf", "hello"));

        var job = await _h.GetAsync(result.JobId!.Value);
        Assert.Null(job.PrinterName);
        Assert.Equal(_h.Inbox, job.OriginDirectory);
        Assert.Equal(Path.Combine(_h.Inbox, "processing", job.Id.ToString("N"), "a.pdf"), job.DocumentPath);
    }

    [Fact]
    public async Task AFileThatVanishedIsSkipped()
    {
        var file = _h.Drop("Office", "a.pdf", "hello");
        File.Delete(file.FilePath);

        var result = await _h.IntakeAsync(file);

        Assert.Equal(IntakeOutcome.Skipped, result.Outcome);
        Assert.Empty(await _h.Queue.Store.ListAsync(JobState.Pending, CancellationToken.None));
    }

    [Fact]
    public async Task AFileThatCannotBeReadIsSkippedAndLeftInPlace()
    {
        var file = _h.Drop("Office", "a.pdf", "hello");
        _h.Files.FailHash = true;

        var result = await _h.IntakeAsync(file);

        Assert.Equal(IntakeOutcome.Skipped, result.Outcome);
        Assert.True(File.Exists(file.FilePath));
    }

    [Fact]
    public async Task AnIdenticalFileArrivingAgainIsADuplicateAndGoesToTheDuplicatesFolder()
    {
        var first = await _h.IntakeAsync(_h.Drop("Office", "a.pdf", "hello", Old));
        var again = _h.Drop("Office", "a.pdf", "hello", Old);

        var result = await _h.IntakeAsync(again);

        Assert.Equal(IntakeOutcome.Duplicate, result.Outcome);
        Assert.Equal(first.JobId, result.JobId);
        Assert.False(File.Exists(again.FilePath));
        Assert.True(File.Exists(Path.Combine(_h.Folder("Office", "duplicates"), "a.pdf")));
        Assert.Single(await _h.Queue.Store.ListAsync(JobState.Pending, CancellationToken.None));
    }

    [Fact]
    public async Task RepeatedDuplicatesDoNotOverwriteEachOther()
    {
        await _h.IntakeAsync(_h.Drop("Office", "a.pdf", "hello", Old));

        await _h.IntakeAsync(_h.Drop("Office", "a.pdf", "hello", Old));
        await _h.IntakeAsync(_h.Drop("Office", "a.pdf", "hello", Old));

        var names = Directory.GetFiles(_h.Folder("Office", "duplicates")).Select(path => Path.GetFileName(path)!).Order().ToArray();
        Assert.Equal(["a (1).pdf", "a.pdf"], names);
    }

    [Fact]
    public async Task ASameContentFileWithANewerDateReplacesAPendingJob()
    {
        var first = await _h.IntakeAsync(_h.Drop("Office", "a.pdf", "hello", Old));
        var firstJob = await _h.GetAsync(first.JobId!.Value);

        var result = await _h.IntakeAsync(_h.Drop("Office", "a.pdf", "hello", Newer));

        Assert.Equal(IntakeOutcome.Enqueued, result.Outcome);
        Assert.Equal([firstJob.Id], result.ReplacedJobIds);
        var replaced = await _h.GetAsync(firstJob.Id);
        Assert.Equal(JobState.Failed, replaced.State);
        Assert.Equal("Replaced by a new arrival of the same file.", replaced.LastError);
        Assert.Equal(Path.Combine(_h.Folder("Office", "errors"), "a.pdf"), replaced.DocumentPath);
        Assert.True(File.Exists(replaced.DocumentPath));
        Assert.False(Directory.Exists(Path.GetDirectoryName(firstJob.DocumentPath)));
        var current = Assert.Single(await _h.Queue.Store.ListAsync(JobState.Pending, CancellationToken.None));
        Assert.Equal(result.JobId, current.Id);
    }

    [Fact]
    public async Task ASameContentFileWithAnOlderDateIsADuplicate()
    {
        await _h.IntakeAsync(_h.Drop("Office", "a.pdf", "hello", Newer));

        var result = await _h.IntakeAsync(_h.Drop("Office", "a.pdf", "hello", Old));

        Assert.Equal(IntakeOutcome.Duplicate, result.Outcome);
    }

    [Fact]
    public async Task ChangedContentReplacesAPendingJobAsANewVersion()
    {
        var first = await _h.IntakeAsync(_h.Drop("Office", "a.pdf", "version one", Old));

        var result = await _h.IntakeAsync(_h.Drop("Office", "a.pdf", "version two", Old));

        Assert.Equal(IntakeOutcome.Enqueued, result.Outcome);
        var replaced = await _h.GetAsync(first.JobId!.Value);
        Assert.Equal(JobState.Failed, replaced.State);
        Assert.Equal("Replaced by a newer version of the file.", replaced.LastError);
        Assert.Equal(Sha("version two"), (await _h.GetAsync(result.JobId!.Value)).FileHash);
    }

    [Fact]
    public async Task ASameContentFileIsADuplicateWhileTheJobIsPrintingEvenWithANewerDate()
    {
        var first = await _h.IntakeAsync(_h.Drop("Office", "a.pdf", "hello", Old));
        await _h.StartPrintingAsync(first.JobId!.Value);

        var result = await _h.IntakeAsync(_h.Drop("Office", "a.pdf", "hello", Newer));

        Assert.Equal(IntakeOutcome.Duplicate, result.Outcome);
        Assert.Equal(JobState.Printing, (await _h.GetAsync(first.JobId.Value)).State);
        Assert.Empty(await _h.Queue.Store.ListAsync(JobState.Pending, CancellationToken.None));
    }

    [Fact]
    public async Task ChangedContentWhilePrintingIsQueuedAndThePrintingJobFinishesUntouched()
    {
        var first = await _h.IntakeAsync(_h.Drop("Office", "a.pdf", "version one", Old));
        await _h.StartPrintingAsync(first.JobId!.Value);

        var result = await _h.IntakeAsync(_h.Drop("Office", "a.pdf", "version two", Newer));

        Assert.Equal(IntakeOutcome.Enqueued, result.Outcome);
        Assert.Empty(result.ReplacedJobIds);
        Assert.Equal(JobState.Printing, (await _h.GetAsync(first.JobId.Value)).State);
        Assert.Equal(JobState.Pending, (await _h.GetAsync(result.JobId!.Value)).State);
    }

    [Theory]
    [InlineData(JobState.Failed)]
    [InlineData(JobState.Completed)]
    [InlineData(JobState.Stalled)]
    public async Task AFinishedJobNeverBlocksTheSameFileFromBeingPrintedAgain(JobState finished)
    {
        var first = await _h.IntakeAsync(_h.Drop("Office", "a.pdf", "hello", Old));
        var id = first.JobId!.Value;
        if (finished == JobState.Failed)
        {
            await _h.Queue.Store.FailAsync(id, "boom", CancellationToken.None);
        }
        else
        {
            await _h.StartPrintingAsync(id);
            await (finished == JobState.Completed
                ? _h.Queue.Store.CompleteAsync(id, CancellationToken.None)
                : _h.Queue.Store.StallAsync(id, "stalled", CancellationToken.None));
        }

        var result = await _h.IntakeAsync(_h.Drop("Office", "a.pdf", "hello", Old));

        Assert.Equal(IntakeOutcome.Enqueued, result.Outcome);
    }

    [Fact]
    public async Task DifferentNamesOrFoldersAreIndependentJobs()
    {
        await _h.IntakeAsync(_h.Drop("Office", "a.pdf", "hello", Old));

        var otherName = await _h.IntakeAsync(_h.Drop("Office", "b.pdf", "hello", Old));
        var otherPrinter = await _h.IntakeAsync(_h.Drop("Labels", "a.pdf", "hello", Old));

        Assert.Equal(IntakeOutcome.Enqueued, otherName.Outcome);
        Assert.Equal(IntakeOutcome.Enqueued, otherPrinter.Outcome);
        Assert.Equal(3, (await _h.Queue.Store.ListAsync(JobState.Pending, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task AFailedMoveFailsTheJobAndLeavesTheFileInTheOrigin()
    {
        var file = _h.Drop("Office", "a.pdf", "hello");
        _h.Files.FailMove = true;

        var result = await _h.IntakeAsync(file);

        Assert.Equal(IntakeOutcome.MoveFailed, result.Outcome);
        var job = await _h.GetAsync(result.JobId!.Value);
        Assert.Equal(JobState.Failed, job.State);
        Assert.Contains("disk full", job.LastError, StringComparison.Ordinal);
        Assert.True(File.Exists(file.FilePath));
    }

    [Fact]
    public async Task ADuplicateThatCannotBeMovedIsStillReportedAsADuplicate()
    {
        await _h.IntakeAsync(_h.Drop("Office", "a.pdf", "hello", Old));
        var again = _h.Drop("Office", "a.pdf", "hello", Old);
        _h.Files.FailMove = true;

        var result = await _h.IntakeAsync(again);

        Assert.Equal(IntakeOutcome.Duplicate, result.Outcome);
        Assert.True(File.Exists(again.FilePath));
    }

    [Fact]
    public async Task ARaceOnTheUniqueKeyIsTreatedAsADuplicateAndNothingIsMoved()
    {
        var racing = new JobIntake(new RacingJobStore(_h.Queue.Store), _h.Files, _h.Stalls, NullLogger<JobIntake>.Instance);
        var file = _h.Drop("Office", "a.pdf", "hello");

        var result = await racing.IntakeAsync(file, CancellationToken.None);

        Assert.Equal(IntakeOutcome.Duplicate, result.Outcome);
        Assert.Null(result.JobId);
        Assert.True(File.Exists(file.FilePath));
    }

    [Fact]
    public async Task APrintingJobStalledForAnHourIsEndedWhenTheSameFileArrivesAndTheNewOneIsQueued()
    {
        var first = await _h.IntakeAsync(_h.Drop("Office", "a.pdf", "hello", Old));
        await _h.StartPrintingAsync(first.JobId!.Value);
        _h.Queue.Time.Advance(TimeSpan.FromHours(1));

        var result = await _h.IntakeAsync(_h.Drop("Office", "a.pdf", "hello", Newer));

        Assert.Equal(IntakeOutcome.Enqueued, result.Outcome);
        var stalled = await _h.GetAsync(first.JobId.Value);
        Assert.Equal(JobState.Stalled, stalled.State);
        Assert.Equal(Path.Combine(_h.Folder("Office", "stalled"), "a.pdf"), stalled.DocumentPath);
        Assert.True(File.Exists(stalled.DocumentPath));
    }

    [Fact]
    public async Task APrintingJobWhosePrinterIsOfflineStaysActiveAndTheNewFileIsADuplicate()
    {
        var first = await _h.IntakeAsync(_h.Drop("Office", "a.pdf", "hello", Old));
        await _h.StartPrintingAsync(first.JobId!.Value);
        _h.Backend.Printers[0] = new PrinterInfo("Office", IsDefault: true, PrinterAvailability.Offline);
        _h.Queue.Time.Advance(TimeSpan.FromHours(3));

        var result = await _h.IntakeAsync(_h.Drop("Office", "a.pdf", "hello", Newer));

        Assert.Equal(IntakeOutcome.Duplicate, result.Outcome);
        Assert.Equal(JobState.Printing, (await _h.GetAsync(first.JobId.Value)).State);
    }

    [Fact]
    public async Task ArgumentsAreValidated()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _h.Intake.IntakeAsync(null!, CancellationToken.None));
        Assert.Throws<ArgumentNullException>(() => new JobIntake(null!, _h.Files, _h.Stalls, NullLogger<JobIntake>.Instance));
        Assert.Throws<ArgumentNullException>(() => new JobIntake(_h.Queue.Store, null!, _h.Stalls, NullLogger<JobIntake>.Instance));
        Assert.Throws<ArgumentNullException>(() => new JobIntake(_h.Queue.Store, _h.Files, null!, NullLogger<JobIntake>.Instance));
        Assert.Throws<ArgumentNullException>(() => new JobIntake(_h.Queue.Store, _h.Files, _h.Stalls, null!));
    }
}

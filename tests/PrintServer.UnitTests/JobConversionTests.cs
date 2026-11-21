using PrintServer.Core.Printing;
using PrintServer.Core.Queue;

namespace PrintServer.UnitTests;

public sealed class JobConversionTests : IDisposable
{
    private readonly QueueHarness _h = new();

    public void Dispose() => _h.Dispose();

    private async Task<Job> QueuedAsync(string name, string? printer = "Office")
    {
        var result = await _h.IntakeAsync(_h.Drop(printer, name, $"content of {name}"));
        return await _h.GetAsync(result.JobId!.Value);
    }

    private Task RunAsync() => _h.Processor.RunOnceAsync(CancellationToken.None);

    private bool InFolder(string folder, string name) =>
        Directory.GetFiles(_h.Folder("Office", folder), name, SearchOption.AllDirectories).Length == 1;

    [Fact]
    public async Task ADocumentIsConvertedAndThePdfIsPrinted()
    {
        var job = await QueuedAsync("report.docx");

        await RunAsync();

        var call = Assert.Single(_h.Converter.Calls);
        Assert.Equal(job.DocumentPath, call.Source);
        Assert.Equal(DocumentFormats.Docx, call.Format);
        Assert.Equal(job.DocumentPath + ".pdf", call.Output);
        var request = Assert.Single(_h.Backend.Requests);
        Assert.Equal(job.DocumentPath + ".pdf", request.FilePath);
        Assert.Equal("report.docx", request.JobName);
        var printing = await _h.GetAsync(job.Id);
        Assert.Equal(JobState.Printing, printing.State);
        Assert.Equal(DocumentFormats.Pdf, printing.DocumentFormat);
        Assert.Equal(DocumentFormats.Docx, printing.SourceFormat);
        Assert.Equal(1, printing.Attempts);
    }

    [Fact]
    public async Task AnImageIsConvertedToo()
    {
        await QueuedAsync("photo.png");

        await RunAsync();

        Assert.Equal(DocumentFormats.Png, Assert.Single(_h.Converter.Calls).Format);
        Assert.EndsWith("photo.png.pdf", Assert.Single(_h.Backend.Requests).FilePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APdfIsPrintedWithoutAnyConversion()
    {
        var job = await QueuedAsync("report.pdf");

        await RunAsync();

        Assert.Empty(_h.Converter.Calls);
        Assert.Equal(job.DocumentPath, Assert.Single(_h.Backend.Requests).FilePath);
    }

    [Fact]
    public async Task OnlyTheOriginalGoesToProcessedAfterPrinting()
    {
        var job = await QueuedAsync("report.docx");
        await RunAsync();
        _h.Backend.Statuses["1"] = JobStatus.Completed;

        await RunAsync();

        Assert.Equal(JobState.Completed, (await _h.GetAsync(job.Id)).State);
        Assert.True(InFolder("processati", "report.docx"));
        Assert.Empty(Directory.GetFiles(_h.Inbox, "*.pdf", SearchOption.AllDirectories));
        Assert.False(Directory.Exists(Path.GetDirectoryName(job.DocumentPath)));
    }

    [Fact]
    public async Task AFailedConversionFailsTheJobAtOnce()
    {
        _h.Converter.FailWith = "The file is password protected.";
        var job = await QueuedAsync("report.docx");

        await RunAsync();

        var failed = await _h.GetAsync(job.Id);
        Assert.Equal(JobState.Failed, failed.State);
        Assert.Equal("The file is password protected.", failed.LastError);
        Assert.Equal(0, failed.Attempts);
        Assert.Empty(_h.Backend.Requests);
        Assert.True(InFolder("errori", "report.docx"));
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(TimeoutException))]
    public async Task AnExceptionFromTheConverterFailsTheJob(Type exceptionType)
    {
        _h.Converter.ThrowOnConvert = (Exception)Activator.CreateInstance(exceptionType, "boom")!;
        var job = await QueuedAsync("report.docx");

        await RunAsync();

        var failed = await _h.GetAsync(job.Id);
        Assert.Equal(JobState.Failed, failed.State);
        Assert.Contains("boom", failed.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFormatNoConverterHandlesFailsTheJob()
    {
        var job = await QueuedAsync("report.odt");

        await RunAsync();

        var failed = await _h.GetAsync(job.Id);
        Assert.Equal(JobState.Failed, failed.State);
        Assert.Contains("cannot be converted", failed.LastError, StringComparison.Ordinal);
        Assert.Empty(_h.Converter.Calls);
    }

    [Fact]
    public async Task ADocumentThatVanishedFailsTheJob()
    {
        var job = await QueuedAsync("report.docx");
        File.Delete(job.DocumentPath);

        await RunAsync();

        var failed = await _h.GetAsync(job.Id);
        Assert.Equal(JobState.Failed, failed.State);
        Assert.Equal("The file of the job is missing.", failed.LastError);
        Assert.Empty(_h.Converter.Calls);
    }

    [Fact]
    public async Task APartialPdfIsRemovedWhenTheConversionFails()
    {
        var job = await QueuedAsync("report.docx");
        await File.WriteAllTextAsync(job.DocumentPath + ".pdf", "partial");
        _h.Converter.FailWith = "timeout";

        await RunAsync();

        Assert.Empty(Directory.GetFiles(_h.Inbox, "*.pdf", SearchOption.AllDirectories));
        Assert.True(InFolder("errori", "report.docx"));
    }

    [Fact]
    public async Task ConversionsRunOneAfterTheOtherInArrivalOrder()
    {
        await QueuedAsync("a.docx");
        await QueuedAsync("b.docx");

        await RunAsync();

        Assert.Equal(["a.docx", "b.docx"], _h.Converter.Calls.Select(call => Path.GetFileName(call.Source)));
    }

    [Fact]
    public async Task AJobWaitingForItsConversionIsNotClaimedForPrinting()
    {
        var docx = SqliteQueueFixture.MakeJob("a.docx");
        var pdf = SqliteQueueFixture.MakeJob("b.pdf", hash: "hash-2");
        await _h.Queue.Store.TryEnqueueAsync(docx, CancellationToken.None);
        await _h.Queue.Store.TryEnqueueAsync(pdf, CancellationToken.None);

        var claimed = await _h.Queue.Store.ClaimNextAsync(CancellationToken.None);

        Assert.Equal(pdf.Id, claimed!.Id);
        Assert.Null(await _h.Queue.Store.ClaimNextAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ConversionClaimsTheOldestJobThatNeedsIt()
    {
        var first = SqliteQueueFixture.MakeJob("a.docx", hash: "hash-1");
        var second = SqliteQueueFixture.MakeJob("b.docx", hash: "hash-2");
        await _h.Queue.Store.TryEnqueueAsync(first, CancellationToken.None);
        _h.Queue.Time.Advance(TimeSpan.FromSeconds(1));
        await _h.Queue.Store.TryEnqueueAsync(second, CancellationToken.None);
        await _h.Queue.Store.TryEnqueueAsync(SqliteQueueFixture.MakeJob("c.pdf", hash: "hash-3"), CancellationToken.None);

        var claimed = await _h.Queue.Store.ClaimNextConversionAsync(CancellationToken.None);

        Assert.Equal(first.Id, claimed!.Id);
        Assert.Equal(JobState.Converting, claimed.State);
        Assert.NotNull(claimed.StartedAt);
        Assert.Equal(second.Id, (await _h.Queue.Store.ClaimNextConversionAsync(CancellationToken.None))!.Id);
        Assert.Null(await _h.Queue.Store.ClaimNextConversionAsync(CancellationToken.None));
    }

    [Fact]
    public async Task FinishingAConversionMakesTheJobPrintable()
    {
        var job = SqliteQueueFixture.MakeJob("a.docx");
        await _h.Queue.Store.TryEnqueueAsync(job, CancellationToken.None);
        await _h.Queue.Store.ClaimNextConversionAsync(CancellationToken.None);

        Assert.True(await _h.Queue.Store.FinishConversionAsync(job.Id, DocumentFormats.Pdf, CancellationToken.None));

        var finished = (await _h.Queue.Store.GetAsync(job.Id, CancellationToken.None))!;
        Assert.Equal(JobState.Pending, finished.State);
        Assert.Equal(DocumentFormats.Pdf, finished.DocumentFormat);
        Assert.Null(finished.StartedAt);
        Assert.Equal(job.Id, (await _h.Queue.Store.ClaimNextAsync(CancellationToken.None))!.Id);
    }

    [Fact]
    public async Task OnlyAConvertingJobCanFinishItsConversion()
    {
        var job = SqliteQueueFixture.MakeJob("a.docx");
        await _h.Queue.Store.TryEnqueueAsync(job, CancellationToken.None);

        Assert.False(await _h.Queue.Store.FinishConversionAsync(job.Id, DocumentFormats.Pdf, CancellationToken.None));
        Assert.False(await _h.Queue.Store.FinishConversionAsync(Guid.NewGuid(), DocumentFormats.Pdf, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => _h.Queue.Store.FinishConversionAsync(job.Id, " ", CancellationToken.None));
    }

    [Fact]
    public async Task ARestartedConversionIsDoneAgain()
    {
        var job = await QueuedAsync("report.docx");
        _h.Queue.SetState(job.Id, JobState.Converting);

        await _h.Recovery.RecoverAsync(CancellationToken.None);
        await RunAsync();

        Assert.Single(_h.Converter.Calls);
        Assert.Equal(JobState.Printing, (await _h.GetAsync(job.Id)).State);
    }

    [Fact]
    public void ThePrintFileIsTheDocumentOrItsConvertedPdf()
    {
        var pdf = SqliteQueueFixture.MakeJob("a.pdf");
        var docx = SqliteQueueFixture.MakeJob("a.docx");
        var pdfJob = JobMapping_(pdf);
        var docxJob = JobMapping_(docx);

        Assert.Equal(pdf.DocumentPath, JobPaths.PrintFile(pdfJob));
        Assert.Equal(docx.DocumentPath + ".pdf", JobPaths.PrintFile(docxJob));
        Assert.Throws<ArgumentNullException>(() => JobPaths.PrintFile(null!));
        Assert.Throws<ArgumentException>(() => JobPaths.ConvertedFile(" "));
    }

    private static Job JobMapping_(NewJob job) => new(
        job.Id, job.OriginDirectory, job.FileName, job.SourcePath, job.DocumentPath, job.PrinterName, job.FileCreatedUtc,
        job.FileHash, job.FileSize, job.SourceFormat, job.SourceFormat, job.Options, JobState.Pending, 0, null, null, null,
        SqliteQueueFixture.Start, null, null, null);

    [Fact]
    public void DocumentFormatsKnowWhatIsPrintable()
    {
        Assert.True(DocumentFormats.IsPrintable(DocumentFormats.Pdf));
        Assert.True(DocumentFormats.IsPrintable(DocumentFormats.PostScript));
        Assert.False(DocumentFormats.IsPrintable(DocumentFormats.Docx));
        Assert.True(DocumentFormats.IsImage(DocumentFormats.Png));
        Assert.False(DocumentFormats.IsImage(DocumentFormats.Pdf));
        Assert.Contains(".docx", DocumentFormats.Extensions);
    }
}

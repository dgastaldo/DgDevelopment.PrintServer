using System.Net;
using Microsoft.Extensions.Options;
using PrintServer.Backends.Cups;
using PrintServer.Backends.Fake;
using PrintServer.Backends.Ipp;
using PrintServer.Backends.Windows;
using PrintServer.Core.Printing;
using PrintServer.Core.Watching;
using PrintServer.Watching;

namespace PrintServer.UnitTests;

public sealed class CancelAndSubmitTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"printserver-submit-{Guid.NewGuid():N}");
    private readonly ScriptedProcessRunner _runner = new();

    public CancelAndSubmitTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private CupsBackend Cups() => new(_runner, Options.Create(new CupsOptions { CancelPath = "cancel-bin" }));

    [Fact]
    public async Task CupsCancelsAJobByItsId()
    {
        _runner.Returns(0);

        Assert.True(await Cups().CancelJobAsync("Office-42", CancellationToken.None));

        var call = Assert.Single(_runner.Calls);
        Assert.Equal("cancel-bin", call.FileName);
        Assert.Equal(["Office-42"], call.Arguments);
    }

    [Theory]
    [InlineData("cancel: cancel-job failed: Job #42 does not exist.")]
    [InlineData("cancel: cancel-job failed: client-error-not-possible")]
    [InlineData("cancel: Job #42 is already canceled - can't cancel.")]
    [InlineData("cancel: Job #42 is already completed - can't cancel.")]
    public async Task CupsTreatsAJobThatIsGoneAsCancelled(string error)
    {
        _runner.Returns(1, standardError: error);

        Assert.True(await Cups().CancelJobAsync("Office-42", CancellationToken.None));
    }

    [Fact]
    public async Task CupsReportsOtherCancelFailures()
    {
        _runner.Returns(1, standardError: "cancel: Forbidden");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Cups().CancelJobAsync("Office-42", CancellationToken.None));

        Assert.Contains("Forbidden", ex.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<ArgumentException>(() => Cups().CancelJobAsync(" ", CancellationToken.None));
    }

    private static IppBackend Ipp(ScriptedHttpHandler handler)
    {
        var options = new IppOptions();
        options.Printers.Add(new IppPrinterOptions { Name = "Office", Uri = new Uri("ipp://office.local/ipp/print") });
        return new IppBackend(new HttpClient(handler), Options.Create(options), disposeHttpClient: true);
    }

    [Fact]
    public async Task IppSendsACancelJobRequest()
    {
        var handler = new ScriptedHttpHandler().Respond(IppResponseWriter.Status(0));
        using var backend = Ipp(handler);

        Assert.True(await backend.CancelJobAsync("12@Office", CancellationToken.None));

        var body = Assert.Single(handler.Requests).Body;
        Assert.Equal([0x02, 0x00, 0x00, 0x08], body[..4]);
        Assert.Contains("job-id", System.Text.Encoding.ASCII.GetString(body), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData((ushort)0x0404)]
    [InlineData((ushort)0x0406)]
    [InlineData((ushort)0x0407)]
    public async Task IppTreatsAJobThatIsGoneAsCancelled(ushort status)
    {
        var handler = new ScriptedHttpHandler().Respond(IppResponseWriter.Status(status));
        using var backend = Ipp(handler);

        Assert.True(await backend.CancelJobAsync("12@Office", CancellationToken.None));
    }

    [Fact]
    public async Task IppReportsOtherCancelFailuresAndBadIds()
    {
        var handler = new ScriptedHttpHandler()
            .Respond(IppResponseWriter.Status(0x0501))
            .Respond([], HttpStatusCode.InternalServerError);
        using var backend = Ipp(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => backend.CancelJobAsync("12@Office", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => backend.CancelJobAsync("12@Office", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => backend.CancelJobAsync("12@Unknown", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => backend.CancelJobAsync("nonsense", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => backend.CancelJobAsync(" ", CancellationToken.None));
    }

    [Fact]
    public async Task TheSpoolerCancelsAJobOnItsPrinter()
    {
        var spooler = new FakeSpoolerApi();
        var backend = new WindowsSpoolerBackend(spooler);

        Assert.True(await backend.CancelJobAsync("7@Office", CancellationToken.None));
        spooler.CancelResult = false;
        Assert.False(await backend.CancelJobAsync("8@Office", CancellationToken.None));

        Assert.Equal([("Office", 7), ("Office", 8)], spooler.Cancelled);
        await Assert.ThrowsAsync<ArgumentException>(() => backend.CancelJobAsync("nonsense", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => backend.CancelJobAsync(" ", CancellationToken.None));
    }

    [Fact]
    public async Task TheFakeBackendForgetsACancelledJob()
    {
        var file = Path.Combine(_directory, "a.pdf");
        await File.WriteAllTextAsync(file, "x");
        var backend = new FakePrinterBackend(Options.Create(new FakePrinterOptions()), TimeProvider.System);
        var id = (await backend.PrintAsync(new PrintRequest(file, "Fake Printer"), CancellationToken.None)).BackendJobId!;

        Assert.True(await backend.CancelJobAsync(id, CancellationToken.None));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => backend.GetJobStatusAsync(id, CancellationToken.None));
        Assert.True(await backend.CancelJobAsync("unknown", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => backend.CancelJobAsync(" ", CancellationToken.None));
    }

    private FolderJobSubmitter Submitter(Action<WatcherOptions>? configure = null)
    {
        var options = new WatcherOptions { Path = _directory };
        configure?.Invoke(options);
        return new FolderJobSubmitter(Options.Create(options));
    }

    private static MemoryStream Content(string text = "content") => new(System.Text.Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task AFileLandsInTheFolderOfItsPrinter()
    {
        using var content = Content("hello");

        var result = await Submitter().SubmitAsync("report.pdf", content, "Office", PrintOptions.Default, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(_directory, "Office", "report.pdf")));
        Assert.Empty(Directory.GetFiles(_directory, "*.part", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task AFileWithoutAPrinterLandsInTheRoot()
    {
        using var content = Content();

        Assert.True((await Submitter().SubmitAsync("report.docx", content, null, PrintOptions.Default, CancellationToken.None)).Succeeded);
        Assert.True(File.Exists(Path.Combine(_directory, "report.docx")));
    }

    [Theory]
    [InlineData(PageOrientation.Landscape, DuplexMode.TwoSidedLongEdge, "orizzontale", "fronte-retro")]
    [InlineData(PageOrientation.Portrait, DuplexMode.Default, "verticale", null)]
    [InlineData(PageOrientation.Default, DuplexMode.OneSided, "fronte", null)]
    [InlineData(PageOrientation.Default, DuplexMode.TwoSidedShortEdge, "fronte-retro-corto", null)]
    public async Task TheOptionsBecomeFoldersTheWatcherUnderstands(PageOrientation orientation, DuplexMode duplex, string first, string? second)
    {
        using var content = Content();
        var options = new PrintOptions(orientation, duplex);

        Assert.True((await Submitter().SubmitAsync("a.pdf", content, "Office", options, CancellationToken.None)).Succeeded);

        var expected = Path.Combine([_directory, "Office", first, .. second is null ? Array.Empty<string>() : [second], "a.pdf"]);
        Assert.True(File.Exists(expected));
        var scanned = new FolderScanner(new PhysicalFileSystem(), new FileFilter(new WatcherOptions()), new WatcherOptions()).Scan(_directory);
        Assert.Equal(options, Assert.Single(scanned).Options);
    }

    [Fact]
    public async Task OptionsWithoutAPrinterUseTheRootOptionFolders()
    {
        using var content = Content();

        await Submitter().SubmitAsync("a.pdf", content, null, new PrintOptions(PageOrientation.Landscape), CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(_directory, "orizzontale", "a.pdf")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("../a.pdf")]
    [InlineData("sub/a.pdf")]
    [InlineData("a\0.pdf")]
    [InlineData(".hidden.pdf")]
    [InlineData("~tmp.pdf")]
    public async Task UnsafeFileNamesAreRefused(string name)
    {
        using var content = Content();

        var result = await Submitter().SubmitAsync(name, content, "Office", PrintOptions.Default, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(Directory.GetFileSystemEntries(_directory, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task TooLongFileNamesAreRefused()
    {
        using var content = Content();

        var result = await Submitter().SubmitAsync(new string('a', 300) + ".pdf", content, null, PrintOptions.Default, CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("virus.exe")]
    [InlineData("notes.zip")]
    [InlineData("noextension")]
    [InlineData("a.pdf.part")]
    public async Task OnlyAcceptedTypesAreStored(string name)
    {
        using var content = Content();

        var result = await Submitter().SubmitAsync(name, content, "Office", PrintOptions.Default, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("not accepted", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheAcceptedExtensionsFollowTheWatcherConfiguration()
    {
        var submitter = Submitter(options => options.Extensions = ["pdf", ".PNG"]);
        using var content = Content();

        Assert.Equal([".pdf", ".PNG"], submitter.AcceptedExtensions);
        Assert.False((await submitter.SubmitAsync("a.docx", content, null, PrintOptions.Default, CancellationToken.None)).Succeeded);
        Assert.Contains(".docx", Submitter().AcceptedExtensions);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("processati")]
    [InlineData("ERRORI")]
    [InlineData("orizzontale")]
    [InlineData(" Office")]
    public async Task PrinterNamesThatCannotBeFoldersAreRefused(string printer)
    {
        using var content = Content();

        var result = await Submitter().SubmitAsync("a.pdf", content, printer, PrintOptions.Default, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("printer name", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnlyOneCopyCanBeRequested()
    {
        using var content = Content();

        var result = await Submitter().SubmitAsync("a.pdf", content, null, new PrintOptions(Copies: 2), CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ASecondFileWithTheSameNameIsRefusedAndTheFirstIsKept()
    {
        using var first = Content("one");
        using var second = Content("two");
        var submitter = Submitter();

        await submitter.SubmitAsync("a.pdf", first, "Office", PrintOptions.Default, CancellationToken.None);
        var result = await submitter.SubmitAsync("a.pdf", second, "Office", PrintOptions.Default, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("already waiting", result.Error, StringComparison.Ordinal);
        Assert.Equal("one", await File.ReadAllTextAsync(Path.Combine(_directory, "Office", "a.pdf")));
    }

    [Fact]
    public async Task ACancelledUploadLeavesNothingBehind()
    {
        using var content = new MemoryStream(new byte[100_000]);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Submitter().SubmitAsync("a.pdf", content, "Office", PrintOptions.Default, cts.Token));

        Assert.Empty(Directory.GetFiles(_directory, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task AnUnwritableFolderIsReportedWithoutCrashing()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, "Office"), "a file where the folder should be");
        using var content = Content();

        var result = await Submitter().SubmitAsync("a.pdf", content, "Office", PrintOptions.Default, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("could not be saved", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSubmitterNeedsTheWatchedFolderAndChecksItsArguments()
    {
        Assert.Throws<InvalidOperationException>(() => new FolderJobSubmitter(Options.Create(new WatcherOptions())));
        Assert.Throws<ArgumentNullException>(() => new FolderJobSubmitter(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Submitter().SubmitAsync("a.pdf", null!, null, PrintOptions.Default, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Submitter().SubmitAsync("a.pdf", Stream.Null, null, null!, CancellationToken.None));
    }

    [Fact]
    public void ResultsKnowTheirOutcome()
    {
        Assert.True(SubmissionResult.Success().Succeeded);
        Assert.False(SubmissionResult.Failure("no").Succeeded);
        Assert.Throws<ArgumentException>(() => SubmissionResult.Failure(" "));
        Assert.Equal("verticale", PrintOptionFolders.FolderFor(PageOrientation.Portrait));
        Assert.Null(PrintOptionFolders.FolderFor(PageOrientation.Default));
        Assert.Null(PrintOptionFolders.FolderFor(DuplexMode.Default));
    }
}

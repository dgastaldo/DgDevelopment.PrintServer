using System.Text;
using Microsoft.Extensions.Options;
using PrintServer.Backends.Cups;
using PrintServer.Backends.Ipp;
using PrintServer.Backends.Windows;
using PrintServer.Core.Printing;
using PrintServer.Core.Queue;
using PrintServer.Queue;
using PrintServer.Watching;

namespace PrintServer.UnitTests;

public sealed class PrintOptionsTests : IDisposable
{
    private static readonly DateTimeOffset Stamp = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "printserver-options-root");
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"printserver options {Guid.NewGuid():N}.pdf");
    private readonly FakeFileSystem _fileSystem = new();
    private readonly QueueHarness _h = new();

    public PrintOptionsTests() => File.WriteAllText(_file, "PDFDATA");

    public void Dispose()
    {
        File.Delete(_file);
        _h.Dispose();
    }

    private string InRoot(params string[] parts) => Path.Combine([_root, .. parts]);

    private IReadOnlyList<ScannedFile> Scan()
    {
        var options = new WatcherOptions();
        return new FolderScanner(_fileSystem, new FileFilter(options), options).Scan(_root);
    }

    [Theory]
    [InlineData("portrait", PageOrientation.Portrait, DuplexMode.Default)]
    [InlineData("landscape", PageOrientation.Landscape, DuplexMode.Default)]
    [InlineData("LANDSCAPE", PageOrientation.Landscape, DuplexMode.Default)]
    [InlineData("one-sided", PageOrientation.Default, DuplexMode.OneSided)]
    [InlineData("two-sided-long-edge", PageOrientation.Default, DuplexMode.TwoSidedLongEdge)]
    [InlineData("two-sided-short-edge", PageOrientation.Default, DuplexMode.TwoSidedShortEdge)]
    public void FolderNamesMapToOptions(string name, PageOrientation orientation, DuplexMode duplex)
    {
        Assert.True(PrintOptionFolders.TryApply(name, PrintOptions.Default, out var applied));

        Assert.Equal(new PrintOptions(orientation, duplex), applied);
    }

    [Theory]
    [InlineData("Ufficio")]
    [InlineData("processed")]
    [InlineData("")]
    public void OtherFolderNamesAreNotOptions(string name)
    {
        Assert.False(PrintOptionFolders.TryApply(name, PrintOptions.Default, out _));
    }

    [Fact]
    public void ApplyingAFolderKeepsTheOtherOption()
    {
        var current = new PrintOptions(PageOrientation.Landscape, DuplexMode.Default, 2);

        PrintOptionFolders.TryApply("two-sided-long-edge", current, out var applied);

        Assert.Equal(new PrintOptions(PageOrientation.Landscape, DuplexMode.TwoSidedLongEdge, 2), applied);
        Assert.Throws<ArgumentNullException>(() => PrintOptionFolders.TryApply(null!, current, out _));
        Assert.Throws<ArgumentNullException>(() => PrintOptionFolders.TryApply("one-sided", null!, out _));
        Assert.Equal(5, PrintOptionFolders.All.Count);
    }

    [Fact]
    public void AnOptionFolderUnderAPrinterSetsTheOptionsOfItsFiles()
    {
        _fileSystem.SetFile(InRoot("Office", "plain.pdf"), 1, Stamp);
        _fileSystem.SetFile(InRoot("Office", "landscape", "wide.pdf"), 1, Stamp);

        var files = Scan().ToDictionary(file => Path.GetFileName(file.Entry.Path));

        Assert.Equal(PrintOptions.Default, files["plain.pdf"].Options);
        Assert.Equal(new PrintOptions(PageOrientation.Landscape), files["wide.pdf"].Options);
        Assert.All(files.Values, file => Assert.Equal("Office", file.PrinterName));
    }

    [Fact]
    public void NestedOptionFoldersCombineInAnyOrder()
    {
        _fileSystem.SetFile(InRoot("Office", "landscape", "two-sided-long-edge", "a.pdf"), 1, Stamp);
        _fileSystem.SetFile(InRoot("Office", "two-sided-long-edge", "landscape", "b.pdf"), 1, Stamp);

        var files = Scan();

        Assert.Equal(2, files.Count);
        Assert.All(files, file => Assert.Equal(new PrintOptions(PageOrientation.Landscape, DuplexMode.TwoSidedLongEdge), file.Options));
    }

    [Fact]
    public void TheInnermostFolderWinsOnAConflict()
    {
        _fileSystem.SetFile(InRoot("Office", "landscape", "portrait", "a.pdf"), 1, Stamp);

        Assert.Equal(PageOrientation.Portrait, Assert.Single(Scan()).Options.Orientation);
    }

    [Fact]
    public void OptionFoldersNestedTooDeepAreIgnored()
    {
        _fileSystem.SetFile(InRoot("Office", "landscape", "one-sided", "portrait", "a.pdf"), 1, Stamp);

        Assert.Empty(Scan());
    }

    [Fact]
    public void AnOptionFolderInTheRootAppliesToTheDefaultPrinter()
    {
        _fileSystem.SetFile(InRoot("two-sided-long-edge", "a.pdf"), 1, Stamp);
        _fileSystem.SetFile(InRoot("two-sided-long-edge", "landscape", "b.pdf"), 1, Stamp);

        var files = Scan().ToDictionary(file => Path.GetFileName(file.Entry.Path));

        Assert.All(files.Values, file => Assert.Null(file.PrinterName));
        Assert.Equal(new PrintOptions(PageOrientation.Default, DuplexMode.TwoSidedLongEdge), files["a.pdf"].Options);
        Assert.Equal(new PrintOptions(PageOrientation.Landscape, DuplexMode.TwoSidedLongEdge), files["b.pdf"].Options);
    }

    [Fact]
    public void FoldersOfTheQueueInsideAnOptionFolderAreIgnored()
    {
        _fileSystem.SetFile(InRoot("Office", "landscape", "processed", "done.pdf"), 1, Stamp);
        _fileSystem.SetFile(InRoot("Office", "landscape", "processing", "id", "wip.pdf"), 1, Stamp);
        _fileSystem.SetFile(InRoot("Office", "Other", "a.pdf"), 1, Stamp);

        Assert.Empty(Scan());
    }

    [Fact]
    public async Task TheQueueKeepsTheOptionsOfTheFile()
    {
        var file = _h.Drop("Office", "a.pdf", "content") with { Options = new PrintOptions(PageOrientation.Landscape, DuplexMode.TwoSidedShortEdge) };

        var result = await _h.IntakeAsync(file);

        var job = await _h.GetAsync(result.JobId!.Value);
        Assert.Equal(new PrintOptions(PageOrientation.Landscape, DuplexMode.TwoSidedShortEdge), job.Options);
    }

    [Fact]
    public async Task TheWorkerPassesTheOptionsToTheBackend()
    {
        var file = _h.Drop("Office", "a.pdf", "content") with { Options = new PrintOptions(PageOrientation.Portrait, DuplexMode.OneSided) };
        await _h.IntakeAsync(file);

        await _h.Processor.RunOnceAsync(CancellationToken.None);

        var request = Assert.Single(_h.Backend.Requests);
        Assert.Equal(PageOrientation.Portrait, request.Orientation);
        Assert.Equal(DuplexMode.OneSided, request.Duplex);
    }

    [Fact]
    public async Task APermanentRejectionFailsTheJobAtOnce()
    {
        _h.Backend.RejectWith = "cannot apply landscape";
        var result = await _h.IntakeAsync(_h.Drop("Office", "a.pdf", "content"));

        await _h.Processor.RunOnceAsync(CancellationToken.None);

        var job = await _h.GetAsync(result.JobId!.Value);
        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal("cannot apply landscape", job.LastError);
        Assert.Equal(1, job.Attempts);
    }

    [Fact]
    public async Task CupsPassesTheOptionsAsSeparateArguments()
    {
        var runner = new ScriptedProcessRunner();
        runner.Returns(0, "request id is Office-7 (1 file(s))\n");
        var backend = new CupsBackend(runner, Options.Create(new CupsOptions { LpPath = "lp-bin", LpstatPath = "lpstat-bin" }));

        await backend.PrintAsync(new PrintRequest(_file, "Office", 1, "Job", PageOrientation.Landscape, DuplexMode.TwoSidedLongEdge), CancellationToken.None);

        Assert.Equal(
            ["-d", "Office", "-n", "1", "-t", "Job", "-o", "orientation-requested=4", "-o", "sides=two-sided-long-edge", Path.GetFullPath(_file)],
            Assert.Single(runner.Calls).Arguments);
    }

    [Theory]
    [InlineData(PageOrientation.Portrait, DuplexMode.OneSided, "orientation-requested=3", "sides=one-sided")]
    [InlineData(PageOrientation.Default, DuplexMode.TwoSidedShortEdge, null, "sides=two-sided-short-edge")]
    [InlineData(PageOrientation.Landscape, DuplexMode.Default, "orientation-requested=4", null)]
    public async Task CupsAddsOnlyTheRequestedOptions(PageOrientation orientation, DuplexMode duplex, string? expectedOrientation, string? expectedSides)
    {
        var runner = new ScriptedProcessRunner();
        runner.Returns(0, "request id is Office-7 (1 file(s))\n");
        var backend = new CupsBackend(runner, Options.Create(new CupsOptions { LpPath = "lp-bin", LpstatPath = "lpstat-bin" }));

        await backend.PrintAsync(new PrintRequest(_file, "Office", 1, "Job", orientation, duplex), CancellationToken.None);

        var arguments = Assert.Single(runner.Calls).Arguments;
        Assert.Equal(expectedOrientation is not null, arguments.Contains("orientation-requested=3") || arguments.Contains("orientation-requested=4"));
        Assert.Equal(expectedOrientation is null ? 0 : 1, arguments.Count(a => a == expectedOrientation));
        Assert.Equal(expectedSides is null ? 0 : 1, arguments.Count(a => a == expectedSides));
    }

    [Fact]
    public async Task CupsAddsNoOptionByDefault()
    {
        var runner = new ScriptedProcessRunner();
        runner.Returns(0, "request id is Office-7 (1 file(s))\n");
        var backend = new CupsBackend(runner, Options.Create(new CupsOptions { LpPath = "lp-bin", LpstatPath = "lpstat-bin" }));

        await backend.PrintAsync(new PrintRequest(_file, "Office"), CancellationToken.None);

        Assert.DoesNotContain("-o", Assert.Single(runner.Calls).Arguments);
    }

    [Theory]
    [InlineData(PageOrientation.Portrait, DuplexMode.OneSided, "one-sided", 3)]
    [InlineData(PageOrientation.Landscape, DuplexMode.TwoSidedLongEdge, "two-sided-long-edge", 4)]
    [InlineData(PageOrientation.Default, DuplexMode.TwoSidedShortEdge, "two-sided-short-edge", 0)]
    public async Task IppSendsTheRequestedJobAttributes(PageOrientation orientation, DuplexMode duplex, string sides, int orientationValue)
    {
        var handler = new ScriptedHttpHandler().Respond(IppResponseWriter.JobCreated(5));
        using var http = new HttpClient(handler);
        var options = new IppOptions();
        options.Printers.Add(new IppPrinterOptions { Name = "Office", Uri = new Uri("ipp://office.local/ipp/print") });
        using var backend = new IppBackend(http, Options.Create(options));

        var result = await backend.PrintAsync(new PrintRequest(_file, "Office", 1, "Job", orientation, duplex), CancellationToken.None);

        Assert.True(result.Accepted);
        var body = Assert.Single(handler.Requests).Body;
        var text = Encoding.ASCII.GetString(body);
        Assert.Contains("sides", text, StringComparison.Ordinal);
        Assert.Contains(sides, text, StringComparison.Ordinal);
        Assert.Equal(orientationValue != 0, text.Contains("orientation-requested", StringComparison.Ordinal));
        if (orientationValue != 0)
        {
            var name = Encoding.ASCII.GetBytes("orientation-requested");
            var index = body.AsSpan().IndexOf(name);
            Assert.Equal([0x00, 0x04, 0x00, 0x00, 0x00, (byte)orientationValue], body[(index + name.Length)..(index + name.Length + 6)]);
            Assert.Equal(0x23, body[index - 3]);
        }
    }

    [Fact]
    public async Task IppSendsNoLayoutAttributeByDefault()
    {
        var handler = new ScriptedHttpHandler().Respond(IppResponseWriter.JobCreated(5));
        using var http = new HttpClient(handler);
        var options = new IppOptions();
        options.Printers.Add(new IppPrinterOptions { Name = "Office", Uri = new Uri("ipp://office.local/ipp/print") });
        using var backend = new IppBackend(http, Options.Create(options));

        await backend.PrintAsync(new PrintRequest(_file, "Office"), CancellationToken.None);

        var text = Encoding.ASCII.GetString(Assert.Single(handler.Requests).Body);
        Assert.DoesNotContain("orientation-requested", text, StringComparison.Ordinal);
        Assert.DoesNotContain("sides", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PageOrientation.Landscape, DuplexMode.Default)]
    [InlineData(PageOrientation.Default, DuplexMode.TwoSidedLongEdge)]
    public async Task TheSpoolerRejectsLayoutOptionsItCannotApply(PageOrientation orientation, DuplexMode duplex)
    {
        var spooler = new FakeSpoolerApi();
        var backend = new WindowsSpoolerBackend(spooler);

        var result = await backend.PrintAsync(new PrintRequest(_file, "Office", 1, null, orientation, duplex), CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.True(result.IsPermanent);
        Assert.Contains("orientation or duplex", result.Error, StringComparison.Ordinal);
        Assert.Empty(spooler.Submitted);
    }

    [Fact]
    public void RequestsReportWhetherTheyAskForALayout()
    {
        Assert.False(new PrintRequest("f", "p").HasLayoutOptions);
        Assert.True(new PrintRequest("f", "p", Orientation: PageOrientation.Landscape).HasLayoutOptions);
        Assert.True(new PrintRequest("f", "p", Duplex: DuplexMode.OneSided).HasLayoutOptions);
    }

    [Fact]
    public void ResultsKnowWhetherTheirFailureIsPermanent()
    {
        Assert.False(PrintResult.Failure("busy").IsPermanent);
        Assert.False(PrintResult.Success("1").IsPermanent);
        var rejected = PrintResult.Rejected("never");
        Assert.True(rejected.IsPermanent);
        Assert.False(rejected.Accepted);
        Assert.Throws<ArgumentException>(() => PrintResult.Rejected(" "));
    }
}

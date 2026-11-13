using PrintServer.Backends.Windows;
using PrintServer.Core.Printing;

namespace PrintServer.UnitTests;

public sealed class WindowsSpoolerBackendTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"printserver spooler {Guid.NewGuid():N}.pdf");
    private readonly FakeSpoolerApi _spooler = new();

    public WindowsSpoolerBackendTests() => File.WriteAllText(_file, "content");

    public void Dispose() => File.Delete(_file);

    private WindowsSpoolerBackend Create() => new(_spooler);

    [Fact]
    public async Task ListPrintersMapsStatusAndMarksTheDefaultIgnoringCase()
    {
        _spooler.Printers.AddRange([new SpoolerPrinter("Office", 0), new SpoolerPrinter("Label", 0x80), new SpoolerPrinter("Plotter", 0x200)]);
        _spooler.DefaultPrinter = "label";

        var printers = await Create().ListPrintersAsync(CancellationToken.None);

        Assert.Equal(["Office", "Label", "Plotter"], printers.Select(p => p.Name));
        Assert.Equal([false, true, false], printers.Select(p => p.IsDefault));
        Assert.Equal(
            [PrinterAvailability.Ready, PrinterAvailability.Offline, PrinterAvailability.Busy],
            printers.Select(p => p.Availability));
    }

    [Fact]
    public async Task ListPrintersWithoutDefaultMarksNone()
    {
        _spooler.Printers.Add(new SpoolerPrinter("Office", 0));

        var printers = await Create().ListPrintersAsync(CancellationToken.None);

        Assert.False(Assert.Single(printers).IsDefault);
    }

    [Fact]
    public async Task PrintSubmitsTheFileAndReturnsAJobIdThatCarriesThePrinter()
    {
        var result = await Create().PrintAsync(new PrintRequest(_file, "Office", JobName: "Invoice"), CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Equal("7@Office", result.BackendJobId);
        var submitted = Assert.Single(_spooler.Submitted);
        Assert.Equal(("Office", "Invoice", 7L), submitted);
    }

    [Fact]
    public async Task PrintUsesTheFileNameWhenNoJobNameIsGiven()
    {
        await Create().PrintAsync(new PrintRequest(_file, "Office"), CancellationToken.None);

        Assert.Equal(Path.GetFileName(_file), _spooler.Submitted[0].Document);
    }

    [Fact]
    public async Task PrintReportsSpoolerFailures()
    {
        _spooler.SubmitFailure = new InvalidOperationException("Cannot open printer 'Nope': not found");

        var result = await Create().PrintAsync(new PrintRequest(_file, "Nope"), CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Contains("Nope", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrintRejectsInvalidRequestsWithoutTouchingTheSpooler()
    {
        var backend = Create();

        Assert.False((await backend.PrintAsync(new PrintRequest(_file + ".missing", "Office"), CancellationToken.None)).Accepted);
        Assert.False((await backend.PrintAsync(new PrintRequest(_file, " "), CancellationToken.None)).Accepted);
        var copies = await backend.PrintAsync(new PrintRequest(_file, "Office", Copies: 2), CancellationToken.None);
        Assert.False(copies.Accepted);
        Assert.Contains("one copy", copies.Error, StringComparison.Ordinal);
        Assert.Empty(_spooler.Submitted);
    }

    [Theory]
    [InlineData(0x8u, JobStatus.Queued)]
    [InlineData(0x10u, JobStatus.Printing)]
    [InlineData(0x1000u, JobStatus.Completed)]
    [InlineData(0x2u, JobStatus.Error)]
    public async Task StatusMapsTheSpoolerJobFlags(uint flags, JobStatus expected)
    {
        _spooler.JobStatuses[("Office", 7)] = flags;

        Assert.Equal(expected, await Create().GetJobStatusAsync("7@Office", CancellationToken.None));
    }

    [Fact]
    public async Task JobThatLeftTheSpoolerIsCompleted()
    {
        Assert.Equal(JobStatus.Completed, await Create().GetJobStatusAsync("7@Office", CancellationToken.None));
    }

    [Fact]
    public async Task MalformedJobIdThrows()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Create().GetJobStatusAsync("garbage", CancellationToken.None));
    }
}

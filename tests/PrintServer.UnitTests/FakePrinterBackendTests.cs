using Microsoft.Extensions.Options;
using PrintServer.Backends.Fake;
using PrintServer.Core.Printing;

namespace PrintServer.UnitTests;

public sealed class FakePrinterBackendTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"printserver-{Guid.NewGuid():N}.pdf");
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero));

    public FakePrinterBackendTests() => File.WriteAllText(_file, "content");

    public void Dispose() => File.Delete(_file);

    private FakePrinterBackend Create(Action<FakePrinterOptions>? configure = null)
    {
        var options = new FakePrinterOptions();
        configure?.Invoke(options);
        return new FakePrinterBackend(Options.Create(options), _time);
    }

    [Fact]
    public async Task ListPrintersReturnsDefaultPrinterWhenNoneConfigured()
    {
        var backend = Create();

        var printers = await backend.ListPrintersAsync(CancellationToken.None);

        var printer = Assert.Single(printers);
        Assert.Equal("Fake Printer", printer.Name);
        Assert.True(printer.IsDefault);
        Assert.Equal(PrinterAvailability.Ready, printer.Availability);
    }

    [Fact]
    public async Task ListPrintersReturnsConfiguredPrintersWithFirstAsDefault()
    {
        var backend = Create(o =>
        {
            o.Printers.Add("Office");
            o.Printers.Add("Warehouse");
        });

        var printers = await backend.ListPrintersAsync(CancellationToken.None);

        Assert.Equal(["Office", "Warehouse"], printers.Select(p => p.Name));
        Assert.Equal([true, false], printers.Select(p => p.IsDefault));
    }

    [Fact]
    public async Task PrintAcceptsExistingFileOnKnownPrinter()
    {
        var backend = Create();

        var result = await backend.PrintAsync(new PrintRequest(_file, "Fake Printer"), CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.False(string.IsNullOrEmpty(result.BackendJobId));
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task PrintRejectsUnknownPrinter()
    {
        var backend = Create();

        var result = await backend.PrintAsync(new PrintRequest(_file, "Nope"), CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Contains("Nope", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrintRejectsMissingFile()
    {
        var backend = Create();

        var result = await backend.PrintAsync(new PrintRequest(_file + ".missing", "Fake Printer"), CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Contains("does not exist", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrintRejectsInvalidCopies()
    {
        var backend = Create();

        var result = await backend.PrintAsync(new PrintRequest(_file, "Fake Printer", Copies: 0), CancellationToken.None);

        Assert.False(result.Accepted);
    }

    [Fact]
    public async Task JobCompletesImmediatelyWithoutDelays()
    {
        var backend = Create();
        var result = await backend.PrintAsync(new PrintRequest(_file, "Fake Printer"), CancellationToken.None);

        var status = await backend.GetJobStatusAsync(result.BackendJobId!, CancellationToken.None);

        Assert.Equal(JobStatus.Completed, status);
    }

    [Fact]
    public async Task JobMovesFromQueuedToPrintingToCompleted()
    {
        var backend = Create(o =>
        {
            o.QueueDelay = TimeSpan.FromSeconds(2);
            o.PrintDuration = TimeSpan.FromSeconds(3);
        });
        var result = await backend.PrintAsync(new PrintRequest(_file, "Fake Printer"), CancellationToken.None);
        var jobId = result.BackendJobId!;

        Assert.Equal(JobStatus.Queued, await backend.GetJobStatusAsync(jobId, CancellationToken.None));

        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(JobStatus.Printing, await backend.GetJobStatusAsync(jobId, CancellationToken.None));

        _time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(JobStatus.Completed, await backend.GetJobStatusAsync(jobId, CancellationToken.None));
    }

    [Fact]
    public async Task FailureRuleEndsJobInError()
    {
        var backend = Create(o => o.FailureRule = request => request.FilePath == _file ? "paper jam" : null);
        var result = await backend.PrintAsync(new PrintRequest(_file, "Fake Printer"), CancellationToken.None);

        var status = await backend.GetJobStatusAsync(result.BackendJobId!, CancellationToken.None);

        Assert.Equal(JobStatus.Error, status);
    }

    [Fact]
    public async Task JobIdsAreUnique()
    {
        var backend = Create();

        var first = await backend.PrintAsync(new PrintRequest(_file, "Fake Printer"), CancellationToken.None);
        var second = await backend.PrintAsync(new PrintRequest(_file, "Fake Printer"), CancellationToken.None);

        Assert.NotEqual(first.BackendJobId, second.BackendJobId);
    }

    [Fact]
    public async Task UnknownJobThrows()
    {
        var backend = Create();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => backend.GetJobStatusAsync("missing", CancellationToken.None));
    }

    [Fact]
    public async Task CancelledTokenIsHonoured()
    {
        var backend = Create();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => backend.ListPrintersAsync(cts.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => backend.PrintAsync(new PrintRequest(_file, "Fake Printer"), cts.Token));
    }
}

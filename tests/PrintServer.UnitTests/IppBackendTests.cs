using System.Net;
using Microsoft.Extensions.Options;
using PrintServer.Backends.Ipp;
using PrintServer.Core.Printing;

namespace PrintServer.UnitTests;

public sealed class IppBackendTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"printserver ipp {Guid.NewGuid():N}.pdf");
    private readonly ScriptedHttpHandler _handler = new();
    private readonly HttpClient _http;

    public IppBackendTests()
    {
        File.WriteAllText(_file, "PDFDATA");
        _http = new HttpClient(_handler);
    }

    public void Dispose()
    {
        _http.Dispose();
        _handler.Dispose();
        File.Delete(_file);
    }

    private IppBackend Create(Action<IppOptions>? configure = null)
    {
        var options = new IppOptions();
        options.Printers.Add(new IppPrinterOptions { Name = "Office", Uri = new Uri("ipp://office.local/ipp/print") });
        options.Printers.Add(new IppPrinterOptions { Name = "Label", Uri = new Uri("ipps://label.local:8631/ipp/print") });
        configure?.Invoke(options);
        return new IppBackend(_http, Options.Create(options));
    }

    [Fact]
    public async Task ListPrintersReportsStateAcceptanceAndUnreachablePrinters()
    {
        _handler
            .Respond(IppResponseWriter.Printer(3, acceptingJobs: true))
            .Respond(IppResponseWriter.Printer(5, acceptingJobs: true));

        var printers = await Create().ListPrintersAsync(CancellationToken.None);

        Assert.Equal(["Office", "Label"], printers.Select(p => p.Name));
        Assert.Equal([true, false], printers.Select(p => p.IsDefault));
        Assert.Equal([PrinterAvailability.Ready, PrinterAvailability.Offline], printers.Select(p => p.Availability));
        Assert.Equal("http://office.local:631/ipp/print", _handler.Requests[0].Uri!.AbsoluteUri);
        Assert.Equal("https://label.local:8631/ipp/print", _handler.Requests[1].Uri!.AbsoluteUri);
        Assert.Equal("application/ipp", _handler.Requests[0].ContentType);
    }

    [Fact]
    public async Task ListPrintersMapsProcessingToBusyAndRefusedJobsToOffline()
    {
        _handler
            .Respond(IppResponseWriter.Printer(4, acceptingJobs: true))
            .Respond(IppResponseWriter.Printer(3, acceptingJobs: false));

        var printers = await Create().ListPrintersAsync(CancellationToken.None);

        Assert.Equal([PrinterAvailability.Busy, PrinterAvailability.Offline], printers.Select(p => p.Availability));
    }

    [Fact]
    public async Task ListPrintersTreatsFailuresAsOffline()
    {
        _handler
            .Throw(new HttpRequestException("connection refused"))
            .Respond([], HttpStatusCode.InternalServerError);

        var printers = await Create().ListPrintersAsync(CancellationToken.None);

        Assert.All(printers, p => Assert.Equal(PrinterAvailability.Offline, p.Availability));
    }

    [Fact]
    public async Task PrintSendsTheDocumentAfterTheIppHeader()
    {
        _handler.Respond(IppResponseWriter.JobCreated(12));

        var result = await Create().PrintAsync(new PrintRequest(_file, "office", Copies: 2, JobName: "Invoice"), CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Equal("12@Office", result.BackendJobId);
        var body = Assert.Single(_handler.Requests).Body;
        Assert.Equal([0x02, 0x00, 0x00, 0x02], body[..4]);
        Assert.EndsWith("PDFDATA", System.Text.Encoding.ASCII.GetString(body), StringComparison.Ordinal);
        var text = System.Text.Encoding.ASCII.GetString(body);
        Assert.Contains("ipp://office.local:631/ipp/print", text, StringComparison.Ordinal);
        Assert.Contains("Invoice", text, StringComparison.Ordinal);
        Assert.Contains("application/pdf", text, StringComparison.Ordinal);
        Assert.Contains("printserver", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrintUsesTheConfiguredDocumentFormatAndUserName()
    {
        _handler.Respond(IppResponseWriter.JobCreated(1));

        await Create(o =>
        {
            o.DocumentFormat = "application/vnd.cups-raw";
            o.UserName = "kiosk";
        }).PrintAsync(new PrintRequest(_file, "Office"), CancellationToken.None);

        var text = System.Text.Encoding.ASCII.GetString(_handler.Requests[0].Body);
        Assert.Contains("application/vnd.cups-raw", text, StringComparison.Ordinal);
        Assert.Contains("kiosk", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrintReportsIppErrors()
    {
        _handler.Respond(IppResponseWriter.Status(0x040A));

        var result = await Create().PrintAsync(new PrintRequest(_file, "Office"), CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Contains("document-format-not-supported", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrintReportsHttpAndNetworkFailures()
    {
        _handler
            .Respond([], HttpStatusCode.Forbidden)
            .Throw(new HttpRequestException("connection refused"))
            .Respond([1, 2, 3]);
        var backend = Create();

        var forbidden = await backend.PrintAsync(new PrintRequest(_file, "Office"), CancellationToken.None);
        var refused = await backend.PrintAsync(new PrintRequest(_file, "Office"), CancellationToken.None);
        var garbage = await backend.PrintAsync(new PrintRequest(_file, "Office"), CancellationToken.None);

        Assert.Contains("403", forbidden.Error, StringComparison.Ordinal);
        Assert.Contains("connection refused", refused.Error, StringComparison.Ordinal);
        Assert.False(garbage.Accepted);
    }

    [Fact]
    public async Task PrintFailsWhenThePrinterReturnsNoJobId()
    {
        _handler.Respond(IppResponseWriter.Status(0));

        var result = await Create().PrintAsync(new PrintRequest(_file, "Office"), CancellationToken.None);

        Assert.False(result.Accepted);
    }

    [Fact]
    public async Task PrintRejectsInvalidRequestsWithoutNetworkTraffic()
    {
        var backend = Create();

        Assert.False((await backend.PrintAsync(new PrintRequest(_file, "Nope"), CancellationToken.None)).Accepted);
        Assert.False((await backend.PrintAsync(new PrintRequest(_file + ".missing", "Office"), CancellationToken.None)).Accepted);
        Assert.False((await backend.PrintAsync(new PrintRequest(_file, "Office", Copies: 0), CancellationToken.None)).Accepted);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task PrintReportsAnInvalidPrinterUri()
    {
        var backend = Create(o => o.Printers.Add(new IppPrinterOptions { Name = "Broken", Uri = null }));

        var result = await backend.PrintAsync(new PrintRequest(_file, "Broken"), CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Empty(_handler.Requests);
    }

    [Theory]
    [InlineData(3, JobStatus.Queued)]
    [InlineData(4, JobStatus.Queued)]
    [InlineData(5, JobStatus.Printing)]
    [InlineData(6, JobStatus.Error)]
    [InlineData(7, JobStatus.Error)]
    [InlineData(8, JobStatus.Error)]
    [InlineData(9, JobStatus.Completed)]
    public async Task JobStateIsMapped(int state, JobStatus expected)
    {
        _handler.Respond(IppResponseWriter.JobState(state));

        Assert.Equal(expected, await Create().GetJobStatusAsync("12@Office", CancellationToken.None));
        var text = System.Text.Encoding.ASCII.GetString(_handler.Requests[0].Body);
        Assert.Contains("job-state", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JobUnknownToThePrinterIsCompleted()
    {
        _handler.Respond(IppResponseWriter.Status(0x0406));

        Assert.Equal(JobStatus.Completed, await Create().GetJobStatusAsync("12@Office", CancellationToken.None));
    }

    [Fact]
    public async Task StatusThrowsOnOtherIppErrorsAndTransportFailures()
    {
        _handler
            .Respond(IppResponseWriter.Status(0x0502))
            .Throw(new HttpRequestException("down"))
            .Respond(IppResponseWriter.Status(0));
        var backend = Create();

        await Assert.ThrowsAsync<InvalidOperationException>(() => backend.GetJobStatusAsync("12@Office", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => backend.GetJobStatusAsync("12@Office", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => backend.GetJobStatusAsync("12@Office", CancellationToken.None));
    }

    [Fact]
    public async Task StatusRejectsMalformedIdsAndUnknownPrinters()
    {
        var backend = Create();

        await Assert.ThrowsAsync<ArgumentException>(() => backend.GetJobStatusAsync("garbage", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => backend.GetJobStatusAsync("1@Nope", CancellationToken.None));
    }

    [Fact]
    public async Task CallerCancellationIsPropagated()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _handler.Throw(new TaskCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Create().PrintAsync(new PrintRequest(_file, "Office"), cts.Token));
    }

    [Fact]
    public async Task ATimeoutIsReportedAsAFailureNotACancellation()
    {
        _handler.Throw(new TaskCanceledException("timed out"));

        var result = await Create().PrintAsync(new PrintRequest(_file, "Office"), CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Contains("timed out", result.Error, StringComparison.Ordinal);
    }
}

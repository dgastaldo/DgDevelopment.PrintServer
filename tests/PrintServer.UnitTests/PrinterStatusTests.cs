using System.Net;
using Microsoft.Extensions.Options;
using PrintServer.Backends.Common;
using PrintServer.Backends.Cups;
using PrintServer.Backends.Fake;
using PrintServer.Backends.Ipp;
using PrintServer.Backends.Windows;
using PrintServer.Core.Printing;

namespace PrintServer.UnitTests;

public sealed class PrinterStatusTests
{
    [Theory]
    [InlineData("none", PrinterIssues.None)]
    [InlineData("media-empty-error", PrinterIssues.PaperOut)]
    [InlineData("media-needed-error", PrinterIssues.PaperOut)]
    [InlineData("media-empty", PrinterIssues.PaperOut)]
    [InlineData("MEDIA-EMPTY-ERROR", PrinterIssues.PaperOut)]
    [InlineData("media-jam-error", PrinterIssues.PaperJam)]
    [InlineData("toner-empty-error", PrinterIssues.TonerOut)]
    [InlineData("marker-supply-empty-error", PrinterIssues.TonerOut)]
    [InlineData("door-open-error", PrinterIssues.DoorOpen)]
    [InlineData("cover-open-error", PrinterIssues.DoorOpen)]
    [InlineData("interlock-open-error", PrinterIssues.DoorOpen)]
    [InlineData("offline-report", PrinterIssues.Offline)]
    [InlineData("shutdown", PrinterIssues.Offline)]
    [InlineData("media-empty-warning", PrinterIssues.None)]
    [InlineData("media-empty-report", PrinterIssues.None)]
    [InlineData("toner-low-warning", PrinterIssues.None)]
    [InlineData("media-low-report", PrinterIssues.None)]
    [InlineData("moving-to-paused", PrinterIssues.None)]
    [InlineData("connecting-to-device", PrinterIssues.None)]
    [InlineData("spool-area-full-error", PrinterIssues.Error)]
    [InlineData("marker-waste-full-error", PrinterIssues.Error)]
    public void ReasonsMapToIssues(string reason, PrinterIssues expected)
    {
        Assert.Equal(expected, PrinterStateReasons.ToIssues([reason]));
    }

    [Fact]
    public void SeveralReasonsAreCombined()
    {
        var issues = PrinterStateReasons.ToIssues(["media-empty-error", "toner-low-warning", "door-open-error"]);

        Assert.Equal(PrinterIssues.PaperOut | PrinterIssues.DoorOpen, issues);
    }

    [Fact]
    public void NoReasonsMeansNoIssues()
    {
        Assert.Equal(PrinterIssues.None, PrinterStateReasons.ToIssues([]));
        Assert.Throws<ArgumentNullException>(() => PrinterStateReasons.ToIssues(null!));
    }

    [Fact]
    public void CupsAlertsAreAttachedToTheirPrinter()
    {
        const string output =
            "printer Office is idle.  enabled since Fri 13 Nov 2026 10:00:00 CET\n" +
            "\tForm mounted:\n" +
            "\tPrinter types: unknown\n" +
            "\tAlerts: media-empty-error toner-low-warning\n" +
            "printer Labels is idle.  enabled since Fri 13 Nov 2026 10:00:00 CET\n" +
            "\tAlerts: offline-report\n" +
            "printer Clean is idle.  enabled since Fri 13 Nov 2026 10:00:00 CET\n" +
            "system default destination: Office\n";

        var printers = CupsOutputParser.ParsePrinters(output);

        Assert.Equal([PrinterIssues.PaperOut, PrinterIssues.Offline, PrinterIssues.None], printers.Select(p => p.Issues));
        Assert.Equal([true, true, false], printers.Select(p => p.IsBlocked));
    }

    [Fact]
    public void CupsAlertsBeforeAnyPrinterAreIgnored()
    {
        Assert.Empty(CupsOutputParser.ParsePrinters("\tAlerts: media-empty-error\n"));
    }

    [Fact]
    public async Task IppPrintersReportTheirStateReasons()
    {
        var handler = new ScriptedHttpHandler()
            .Respond(IppResponseWriter.Printer(3, acceptingJobs: true, "media-empty-error", "toner-low-warning"))
            .Respond(IppResponseWriter.Printer(3, acceptingJobs: true, "none"));
        using var http = new HttpClient(handler);
        var options = new IppOptions();
        options.Printers.Add(new IppPrinterOptions { Name = "Office", Uri = new Uri("ipp://office.local/ipp/print") });
        options.Printers.Add(new IppPrinterOptions { Name = "Label", Uri = new Uri("ipp://label.local/ipp/print") });
        using var backend = new IppBackend(http, Options.Create(options));

        var printers = await backend.ListPrintersAsync(CancellationToken.None);

        Assert.Equal([PrinterIssues.PaperOut, PrinterIssues.None], printers.Select(p => p.Issues));
        Assert.Equal([true, false], printers.Select(p => p.IsBlocked));
    }

    [Fact]
    public async Task AnIppPrinterNotAcceptingJobsKeepsItsReasons()
    {
        var handler = new ScriptedHttpHandler()
            .Respond(IppResponseWriter.Printer(3, acceptingJobs: false, "door-open-error"));
        using var http = new HttpClient(handler);
        var options = new IppOptions();
        options.Printers.Add(new IppPrinterOptions { Name = "Office", Uri = new Uri("ipp://office.local/ipp/print") });
        using var backend = new IppBackend(http, Options.Create(options));

        var printer = Assert.Single(await backend.ListPrintersAsync(CancellationToken.None));

        Assert.Equal(PrinterAvailability.Offline, printer.Availability);
        Assert.Equal(PrinterIssues.DoorOpen, printer.Issues);
    }

    [Fact]
    public async Task AnUnreachableIppPrinterIsOfflineWithTheOfflineIssue()
    {
        var handler = new ScriptedHttpHandler()
            .Throw(new HttpRequestException("refused"))
            .Respond([], HttpStatusCode.InternalServerError);
        using var http = new HttpClient(handler);
        var options = new IppOptions();
        options.Printers.Add(new IppPrinterOptions { Name = "A", Uri = new Uri("ipp://a.local/ipp/print") });
        options.Printers.Add(new IppPrinterOptions { Name = "B", Uri = new Uri("ipp://b.local/ipp/print") });
        using var backend = new IppBackend(http, Options.Create(options));

        var printers = await backend.ListPrintersAsync(CancellationToken.None);

        Assert.All(printers, p => Assert.Equal(PrinterIssues.Offline, p.Issues));
    }

    [Theory]
    [InlineData(0u, PrinterIssues.None)]
    [InlineData(0x400u, PrinterIssues.None)]
    [InlineData(0x1u, PrinterIssues.Offline)]
    [InlineData(0x80u, PrinterIssues.Offline)]
    [InlineData(0x1000u, PrinterIssues.Offline)]
    [InlineData(0x10u, PrinterIssues.PaperOut)]
    [InlineData(0x40u, PrinterIssues.PaperOut)]
    [InlineData(0x40000u, PrinterIssues.TonerOut)]
    [InlineData(0x8u, PrinterIssues.PaperJam)]
    [InlineData(0x400000u, PrinterIssues.DoorOpen)]
    [InlineData(0x2u, PrinterIssues.Error)]
    [InlineData(0x100000u, PrinterIssues.Error)]
    [InlineData(0x10u | 0x40000u, PrinterIssues.PaperOut | PrinterIssues.TonerOut)]
    public void SpoolerStatusMapsToIssues(uint status, PrinterIssues expected)
    {
        Assert.Equal(expected, SpoolerStatusMapper.MapIssues(status));
    }

    [Fact]
    public async Task TheSpoolerBackendReportsIssues()
    {
        var spooler = new FakeSpoolerApi { DefaultPrinter = "Office" };
        spooler.Printers.Add(new SpoolerPrinter("Office", 0x10));
        spooler.Printers.Add(new SpoolerPrinter("Labels", 0));
        var backend = new WindowsSpoolerBackend(spooler);

        var printers = await backend.ListPrintersAsync(CancellationToken.None);

        Assert.Equal([PrinterIssues.PaperOut, PrinterIssues.None], printers.Select(p => p.Issues));
    }

    [Fact]
    public async Task TheFakeBackendReportsConfiguredIssues()
    {
        var options = new FakePrinterOptions();
        options.Printers.Add("Office");
        options.Printers.Add("Labels");
        options.Issues["labels"] = PrinterIssues.TonerOut;
        var backend = new FakePrinterBackend(Options.Create(options), TimeProvider.System);

        var printers = await backend.ListPrintersAsync(CancellationToken.None);

        Assert.Equal([PrinterIssues.None, PrinterIssues.TonerOut], printers.Select(p => p.Issues));
    }
}

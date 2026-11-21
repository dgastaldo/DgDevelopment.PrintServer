using PrintServer.Core.Printing;
using PrintServer.Core.Queue;

namespace PrintServer.UnitTests;

public sealed class CoreQueueContractsTests
{
    [Fact]
    public void DefaultPrintOptionsAreOnePortraitAgnosticCopy()
    {
        var options = PrintOptions.Default;

        Assert.Equal(PageOrientation.Default, options.Orientation);
        Assert.Equal(DuplexMode.Default, options.Duplex);
        Assert.Equal(1, options.Copies);
    }

    [Theory]
    [InlineData(PrinterAvailability.Ready, PrinterIssues.None, false)]
    [InlineData(PrinterAvailability.Busy, PrinterIssues.None, false)]
    [InlineData(PrinterAvailability.Offline, PrinterIssues.None, true)]
    [InlineData(PrinterAvailability.Ready, PrinterIssues.PaperOut, true)]
    [InlineData(PrinterAvailability.Busy, PrinterIssues.TonerOut | PrinterIssues.DoorOpen, true)]
    public void APrinterIsBlockedWhenOfflineOrReportingAnIssue(PrinterAvailability availability, PrinterIssues issues, bool blocked)
    {
        var printer = new PrinterInfo("Office", IsDefault: false, availability, issues);

        Assert.Equal(blocked, printer.IsBlocked);
    }

    [Fact]
    public void PrinterIssuesDefaultToNoneForExistingCallers()
    {
        var printer = new PrinterInfo("Office", IsDefault: true, PrinterAvailability.Ready);

        Assert.Equal(PrinterIssues.None, printer.Issues);
        Assert.False(printer.IsBlocked);
    }

    [Theory]
    [InlineData("a.pdf", DocumentFormats.Pdf)]
    [InlineData("A.PDF", DocumentFormats.Pdf)]
    [InlineData("a.ps", DocumentFormats.PostScript)]
    [InlineData("a.jpg", DocumentFormats.Jpeg)]
    [InlineData("a.JPEG", DocumentFormats.Jpeg)]
    [InlineData("a.png", DocumentFormats.Png)]
    [InlineData("a.txt", DocumentFormats.PlainText)]
    [InlineData("a.rtf", DocumentFormats.Rtf)]
    [InlineData("a.doc", DocumentFormats.Doc)]
    [InlineData("a.docx", DocumentFormats.Docx)]
    [InlineData("a.odt", DocumentFormats.Odt)]
    [InlineData("a.xls", DocumentFormats.Xls)]
    [InlineData("a.xlsx", DocumentFormats.Xlsx)]
    [InlineData("a.ods", DocumentFormats.Ods)]
    [InlineData("a.ppt", DocumentFormats.Ppt)]
    [InlineData("a.pptx", DocumentFormats.Pptx)]
    [InlineData("a.odp", DocumentFormats.Odp)]
    [InlineData("a.zip", DocumentFormats.Unknown)]
    [InlineData("noextension", DocumentFormats.Unknown)]
    public void FormatsAreDeducedFromTheExtension(string file, string expected)
    {
        Assert.Equal(expected, DocumentFormats.ForFile(file));
    }

    [Fact]
    public void FormatLookupRejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => DocumentFormats.ForFile(null!));
    }

    [Theory]
    [InlineData(JobState.Pending, true)]
    [InlineData(JobState.Converting, true)]
    [InlineData(JobState.Printing, true)]
    [InlineData(JobState.Completed, false)]
    [InlineData(JobState.Failed, false)]
    [InlineData(JobState.Stalled, false)]
    public void OnlyPendingConvertingAndPrintingJobsAreActive(JobState state, bool active)
    {
        Assert.Equal(active, JobStates.IsActive(state));
    }

    [Fact]
    public void TheQueueFoldersAreTheFiveDocumentedOnes()
    {
        Assert.Equal(["processing", "processati", "errori", "stallo", "duplicati"], QueueFolders.All);
    }
}

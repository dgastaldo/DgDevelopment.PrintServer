using PrintServer.Backends.Cups;
using PrintServer.Core.Printing;

namespace PrintServer.UnitTests;

public sealed class CupsOutputParserTests
{
    private const string PrintersOutput =
        "printer Office_Laser is idle.  enabled since Fri 13 Nov 2026 10:00:00 CET\n" +
        "printer Warehouse now printing Warehouse-12.  enabled since Fri 13 Nov 2026 10:05:00 CET\n" +
        "printer Old_Inkjet disabled since Fri 13 Nov 2026 09:00:00 CET -\n" +
        "\treason unknown\n" +
        "system default destination: Warehouse\n";

    [Fact]
    public void ParsePrintersReadsNamesStatesAndDefault()
    {
        var printers = CupsOutputParser.ParsePrinters(PrintersOutput);

        Assert.Equal(["Office_Laser", "Warehouse", "Old_Inkjet"], printers.Select(p => p.Name));
        Assert.Equal(
            [PrinterAvailability.Ready, PrinterAvailability.Busy, PrinterAvailability.Offline],
            printers.Select(p => p.Availability));
        Assert.Equal([false, true, false], printers.Select(p => p.IsDefault));
    }

    [Fact]
    public void ParsePrintersWithoutDefaultMarksNone()
    {
        var printers = CupsOutputParser.ParsePrinters("printer A is idle.  enabled since x\nno system default destination\n");

        Assert.False(Assert.Single(printers).IsDefault);
    }

    [Fact]
    public void ParsePrintersOnEmptyOutputReturnsEmpty()
    {
        Assert.Empty(CupsOutputParser.ParsePrinters(string.Empty));
    }

    [Theory]
    [InlineData("request id is Office_Laser-42 (1 file(s))\n", "Office_Laser-42")]
    [InlineData("request id is A-1 (2 file(s))", "A-1")]
    public void ParseRequestIdReadsJobId(string output, string expected)
    {
        Assert.Equal(expected, CupsOutputParser.ParseRequestId(output));
    }

    [Theory]
    [InlineData("")]
    [InlineData("lp: error")]
    [InlineData("request id is")]
    public void ParseRequestIdReturnsNullWhenMissing(string output)
    {
        Assert.Null(CupsOutputParser.ParseRequestId(output));
    }

    [Fact]
    public void ParsePendingJobIdsReadsEveryJobLine()
    {
        const string output =
            "Office_Laser-41   user   1024   Fri 13 Nov 2026 10:00:00 CET\n" +
            "Warehouse-12      user   2048   Fri 13 Nov 2026 10:01:00 CET\n";

        var ids = CupsOutputParser.ParsePendingJobIds(output);

        Assert.Equal(2, ids.Count);
        Assert.Contains("Office_Laser-41", ids);
        Assert.Contains("Warehouse-12", ids);
    }

    [Theory]
    [InlineData("Office_Laser-41", "Office_Laser")]
    [InlineData("My-Printer-7", "My-Printer")]
    [InlineData("nodash", "nodash")]
    public void PrinterOfJobStripsTheJobNumber(string jobId, string expected)
    {
        Assert.Equal(expected, CupsOutputParser.PrinterOfJob(jobId));
    }

    [Fact]
    public void IsPrintingJobMatchesOnlyTheExactJob()
    {
        const string output = "printer P now printing P-12.  enabled since x\n";

        Assert.True(CupsOutputParser.IsPrintingJob(output, "P-12"));
        Assert.False(CupsOutputParser.IsPrintingJob(output, "P-1"));
    }

    [Fact]
    public void NoDestinationsMessageIsRecognised()
    {
        Assert.True(CupsOutputParser.IsNoDestinations("lpstat: No destinations added."));
        Assert.False(CupsOutputParser.IsNoDestinations("lpstat: Forbidden"));
    }
}

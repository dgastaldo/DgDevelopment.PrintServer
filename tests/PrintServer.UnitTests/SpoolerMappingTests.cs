using PrintServer.Backends.Common;
using PrintServer.Backends.Windows;
using PrintServer.Core.Printing;

namespace PrintServer.UnitTests;

public sealed class SpoolerMappingTests
{
    [Theory]
    [InlineData(0u, PrinterAvailability.Ready)]
    [InlineData(0x200u, PrinterAvailability.Busy)]
    [InlineData(0x400u, PrinterAvailability.Busy)]
    [InlineData(0x4000u, PrinterAvailability.Busy)]
    [InlineData(0x80u, PrinterAvailability.Offline)]
    [InlineData(0x1u, PrinterAvailability.Offline)]
    [InlineData(0x2u, PrinterAvailability.Offline)]
    [InlineData(0x10u, PrinterAvailability.Offline)]
    [InlineData(0x1000u, PrinterAvailability.Offline)]
    [InlineData(0x400u | 0x10u, PrinterAvailability.Offline)]
    public void PrinterStatusMapsToAvailability(uint status, PrinterAvailability expected)
    {
        Assert.Equal(expected, SpoolerStatusMapper.MapPrinter(status));
    }

    [Theory]
    [InlineData(0u, JobStatus.Queued)]
    [InlineData(0x8u, JobStatus.Queued)]
    [InlineData(0x1u, JobStatus.Queued)]
    [InlineData(0x10u, JobStatus.Printing)]
    [InlineData(0x80u, JobStatus.Completed)]
    [InlineData(0x1000u, JobStatus.Completed)]
    [InlineData(0x2u, JobStatus.Error)]
    [InlineData(0x20u, JobStatus.Error)]
    [InlineData(0x40u, JobStatus.Error)]
    [InlineData(0x4u, JobStatus.Error)]
    [InlineData(0x100u, JobStatus.Error)]
    [InlineData(0x400u, JobStatus.Error)]
    [InlineData(0x10u | 0x40u, JobStatus.Error)]
    [InlineData(0x80u | 0x2000u, JobStatus.Completed)]
    public void JobStatusMapsToJobStatus(uint status, JobStatus expected)
    {
        Assert.Equal(expected, SpoolerStatusMapper.MapJob(status));
    }

    [Fact]
    public void JobIdRoundTripsThroughFormatAndParse()
    {
        var id = BackendJobId.Format(42, "Office @ Floor 2");

        Assert.True(BackendJobId.TryParse(id, out var jobId, out var printer));
        Assert.Equal(42, jobId);
        Assert.Equal("Office @ Floor 2", printer);
    }

    [Theory]
    [InlineData("")]
    [InlineData("42")]
    [InlineData("@Printer")]
    [InlineData("abc@Printer")]
    [InlineData("-1@Printer")]
    [InlineData("42@")]
    public void MalformedJobIdsAreRejected(string value)
    {
        Assert.False(BackendJobId.TryParse(value, out _, out _));
    }
}

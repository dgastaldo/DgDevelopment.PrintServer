using PrintServer.Core.Printing;

namespace PrintServer.Backends.Windows;

internal static class SpoolerStatusMapper
{
    private const uint PrinterPaused = 0x1;
    private const uint PrinterError = 0x2;
    private const uint PrinterPendingDeletion = 0x4;
    private const uint PrinterPaperJam = 0x8;
    private const uint PrinterPaperOut = 0x10;
    private const uint PrinterPaperProblem = 0x40;
    private const uint PrinterOffline = 0x80;
    private const uint PrinterIoActive = 0x100;
    private const uint PrinterBusy = 0x200;
    private const uint PrinterPrinting = 0x400;
    private const uint PrinterOutputBinFull = 0x800;
    private const uint PrinterNotAvailable = 0x1000;
    private const uint PrinterWaiting = 0x2000;
    private const uint PrinterProcessing = 0x4000;
    private const uint PrinterInitializing = 0x8000;
    private const uint PrinterWarmingUp = 0x10000;
    private const uint PrinterNoToner = 0x40000;
    private const uint PrinterUserIntervention = 0x100000;
    private const uint PrinterOutOfMemory = 0x200000;
    private const uint PrinterDoorOpen = 0x400000;
    private const uint PrinterServerUnknown = 0x800000;

    private const uint JobError = 0x2;
    private const uint JobDeleting = 0x4;
    private const uint JobPrinting = 0x10;
    private const uint JobOffline = 0x20;
    private const uint JobPaperOut = 0x40;
    private const uint JobPrinted = 0x80;
    private const uint JobDeleted = 0x100;
    private const uint JobBlockedDeviceQueue = 0x200;
    private const uint JobUserIntervention = 0x400;
    private const uint JobComplete = 0x1000;

    private const uint PrinterUnavailableMask =
        PrinterPaused | PrinterError | PrinterPendingDeletion | PrinterPaperJam | PrinterPaperOut |
        PrinterPaperProblem | PrinterOffline | PrinterOutputBinFull | PrinterNotAvailable | PrinterNoToner |
        PrinterUserIntervention | PrinterOutOfMemory | PrinterDoorOpen | PrinterServerUnknown;

    private const uint PrinterBusyMask =
        PrinterIoActive | PrinterBusy | PrinterPrinting | PrinterWaiting | PrinterProcessing |
        PrinterInitializing | PrinterWarmingUp;

    private const uint JobErrorMask =
        JobError | JobDeleting | JobOffline | JobPaperOut | JobDeleted | JobBlockedDeviceQueue | JobUserIntervention;

    public static PrinterAvailability MapPrinter(uint status)
    {
        if ((status & PrinterUnavailableMask) != 0)
        {
            return PrinterAvailability.Offline;
        }

        return (status & PrinterBusyMask) != 0 ? PrinterAvailability.Busy : PrinterAvailability.Ready;
    }

    public static JobStatus MapJob(uint status)
    {
        if ((status & JobErrorMask) != 0)
        {
            return JobStatus.Error;
        }

        if ((status & (JobPrinted | JobComplete)) != 0)
        {
            return JobStatus.Completed;
        }

        return (status & JobPrinting) != 0 ? JobStatus.Printing : JobStatus.Queued;
    }
}

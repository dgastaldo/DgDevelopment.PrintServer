using PrintServer.Core.Printing;

namespace PrintServer.Watching;

internal sealed record ScannedFile(FileEntry Entry, string? PrinterName, PrintOptions Options)
{
    public ScannedFile(FileEntry entry, string? printerName)
        : this(entry, printerName, PrintOptions.Default)
    {
    }
}

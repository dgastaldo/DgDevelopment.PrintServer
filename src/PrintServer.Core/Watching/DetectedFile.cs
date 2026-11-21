using PrintServer.Core.Printing;

namespace PrintServer.Core.Watching;

public sealed record DetectedFile(string FilePath, string? PrinterName, long Length, DateTimeOffset CreatedUtc, PrintOptions Options)
{
    public DetectedFile(string filePath, string? printerName, long length, DateTimeOffset createdUtc)
        : this(filePath, printerName, length, createdUtc, PrintOptions.Default)
    {
    }
}

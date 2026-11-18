namespace PrintServer.Watching;

internal sealed record ScannedFile(FileEntry Entry, string? PrinterName);

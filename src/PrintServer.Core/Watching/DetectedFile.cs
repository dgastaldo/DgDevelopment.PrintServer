namespace PrintServer.Core.Watching;

public sealed record DetectedFile(string FilePath, string? PrinterName, long Length);

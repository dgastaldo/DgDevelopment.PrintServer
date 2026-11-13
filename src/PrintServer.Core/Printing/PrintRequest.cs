namespace PrintServer.Core.Printing;

public sealed record PrintRequest(string FilePath, string PrinterName, int Copies = 1, string? JobName = null);

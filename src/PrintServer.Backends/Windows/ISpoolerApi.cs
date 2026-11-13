namespace PrintServer.Backends.Windows;

public interface ISpoolerApi
{
    IReadOnlyList<SpoolerPrinter> EnumeratePrinters();

    string? GetDefaultPrinterName();

    int SubmitRawDocument(string printerName, string documentName, Stream content, CancellationToken ct);

    uint? GetJobStatus(string printerName, int jobId);
}

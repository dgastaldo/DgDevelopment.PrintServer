using PrintServer.Backends.Windows;

namespace PrintServer.UnitTests;

internal sealed class FakeSpoolerApi : ISpoolerApi
{
    public List<SpoolerPrinter> Printers { get; } = [];

    public string? DefaultPrinter { get; set; }

    public int NextJobId { get; set; } = 7;

    public Exception? SubmitFailure { get; set; }

    public Dictionary<(string Printer, int JobId), uint> JobStatuses { get; } = [];

    public List<(string Printer, string Document, long Length)> Submitted { get; } = [];

    public IReadOnlyList<SpoolerPrinter> EnumeratePrinters() => Printers;

    public string? GetDefaultPrinterName() => DefaultPrinter;

    public int SubmitRawDocument(string printerName, string documentName, Stream content, CancellationToken ct)
    {
        if (SubmitFailure is not null)
        {
            throw SubmitFailure;
        }

        Submitted.Add((printerName, documentName, content.Length));
        return NextJobId;
    }

    public uint? GetJobStatus(string printerName, int jobId) =>
        JobStatuses.TryGetValue((printerName, jobId), out var status) ? status : null;
}

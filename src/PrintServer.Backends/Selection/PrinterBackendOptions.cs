namespace PrintServer.Backends.Selection;

public sealed class PrinterBackendOptions
{
    public const string SectionName = "Printing";

    public PrinterBackendKind Backend { get; set; } = PrinterBackendKind.Fake;
}

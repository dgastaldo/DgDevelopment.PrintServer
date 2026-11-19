namespace PrintServer.Core.Printing;

public sealed record PrinterInfo(
    string Name,
    bool IsDefault,
    PrinterAvailability Availability,
    PrinterIssues Issues = PrinterIssues.None)
{
    public bool IsBlocked => Availability == PrinterAvailability.Offline || Issues != PrinterIssues.None;
}

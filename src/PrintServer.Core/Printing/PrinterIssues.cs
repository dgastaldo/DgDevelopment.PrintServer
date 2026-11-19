namespace PrintServer.Core.Printing;

[Flags]
public enum PrinterIssues
{
    None = 0,
    Offline = 1,
    PaperOut = 2,
    TonerOut = 4,
    PaperJam = 8,
    DoorOpen = 16,
    Error = 32,
}

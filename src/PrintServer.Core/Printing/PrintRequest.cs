namespace PrintServer.Core.Printing;

public sealed record PrintRequest(
    string FilePath,
    string PrinterName,
    int Copies = 1,
    string? JobName = null,
    PageOrientation Orientation = PageOrientation.Default,
    DuplexMode Duplex = DuplexMode.Default)
{
    public bool HasLayoutOptions => Orientation != PageOrientation.Default || Duplex != DuplexMode.Default;
}

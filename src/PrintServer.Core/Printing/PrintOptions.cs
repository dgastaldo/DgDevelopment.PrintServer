namespace PrintServer.Core.Printing;

public enum PageOrientation
{
    Default,
    Portrait,
    Landscape,
}

public enum DuplexMode
{
    Default,
    OneSided,
    TwoSidedLongEdge,
    TwoSidedShortEdge,
}

public sealed record PrintOptions(
    PageOrientation Orientation = PageOrientation.Default,
    DuplexMode Duplex = DuplexMode.Default,
    int Copies = 1)
{
    public static PrintOptions Default { get; } = new();
}

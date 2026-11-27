namespace PrintServer.Core.Printing;

public static class PrintOptionFolders
{
    public const string Portrait = "portrait";

    public const string Landscape = "landscape";

    public const string OneSided = "one-sided";

    public const string TwoSidedLongEdge = "two-sided-long-edge";

    public const string TwoSidedShortEdge = "two-sided-short-edge";

    public static IReadOnlyList<string> All { get; } = [Portrait, Landscape, OneSided, TwoSidedLongEdge, TwoSidedShortEdge];

    public static string? FolderFor(PageOrientation orientation) => orientation switch
    {
        PageOrientation.Portrait => Portrait,
        PageOrientation.Landscape => Landscape,
        _ => null,
    };

    public static string? FolderFor(DuplexMode duplex) => duplex switch
    {
        DuplexMode.OneSided => OneSided,
        DuplexMode.TwoSidedLongEdge => TwoSidedLongEdge,
        DuplexMode.TwoSidedShortEdge => TwoSidedShortEdge,
        _ => null,
    };

    public static bool TryApply(string folderName, PrintOptions current, out PrintOptions applied)
    {
        ArgumentNullException.ThrowIfNull(folderName);
        ArgumentNullException.ThrowIfNull(current);

        applied = folderName.ToUpperInvariant() switch
        {
            "PORTRAIT" => current with { Orientation = PageOrientation.Portrait },
            "LANDSCAPE" => current with { Orientation = PageOrientation.Landscape },
            "ONE-SIDED" => current with { Duplex = DuplexMode.OneSided },
            "TWO-SIDED-LONG-EDGE" => current with { Duplex = DuplexMode.TwoSidedLongEdge },
            "TWO-SIDED-SHORT-EDGE" => current with { Duplex = DuplexMode.TwoSidedShortEdge },
            _ => current,
        };
        return !ReferenceEquals(applied, current);
    }
}

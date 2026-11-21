namespace PrintServer.Core.Printing;

public static class PrintOptionFolders
{
    public const string Portrait = "verticale";

    public const string Landscape = "orizzontale";

    public const string OneSided = "fronte";

    public const string TwoSidedLongEdge = "fronte-retro";

    public const string TwoSidedShortEdge = "fronte-retro-corto";

    public static IReadOnlyList<string> All { get; } = [Portrait, Landscape, OneSided, TwoSidedLongEdge, TwoSidedShortEdge];

    public static bool TryApply(string folderName, PrintOptions current, out PrintOptions applied)
    {
        ArgumentNullException.ThrowIfNull(folderName);
        ArgumentNullException.ThrowIfNull(current);

        applied = folderName.ToUpperInvariant() switch
        {
            "VERTICALE" => current with { Orientation = PageOrientation.Portrait },
            "ORIZZONTALE" => current with { Orientation = PageOrientation.Landscape },
            "FRONTE" => current with { Duplex = DuplexMode.OneSided },
            "FRONTE-RETRO" => current with { Duplex = DuplexMode.TwoSidedLongEdge },
            "FRONTE-RETRO-CORTO" => current with { Duplex = DuplexMode.TwoSidedShortEdge },
            _ => current,
        };
        return !ReferenceEquals(applied, current);
    }
}

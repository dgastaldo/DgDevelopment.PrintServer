namespace PrintServer.WebUi;

/// <summary>Marker for the localized strings of the web page. Keys are in English; the neutral resources are English too.</summary>
public sealed class Strings
{
    private Strings()
    {
    }

    /// <summary>Folder (inside the project) that holds the <c>Strings.*.resx</c> files.</summary>
    public const string ResourcesPath = "Resources";
}

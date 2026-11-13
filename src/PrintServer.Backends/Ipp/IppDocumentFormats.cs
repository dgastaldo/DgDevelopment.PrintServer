namespace PrintServer.Backends.Ipp;

internal static class IppDocumentFormats
{
    public const string Auto = "application/octet-stream";

    public static string ForFile(string path) =>
        Path.GetExtension(path).ToUpperInvariant() switch
        {
            ".PDF" => "application/pdf",
            ".JPG" or ".JPEG" => "image/jpeg",
            ".PNG" => "image/png",
            ".TXT" => "text/plain",
            ".PS" => "application/postscript",
            _ => Auto,
        };
}

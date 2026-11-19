namespace PrintServer.Core.Printing;

public static class DocumentFormats
{
    public const string Pdf = "application/pdf";

    public const string PostScript = "application/postscript";

    public const string Jpeg = "image/jpeg";

    public const string Png = "image/png";

    public const string PlainText = "text/plain";

    public const string Unknown = "application/octet-stream";

    public static string ForFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return Path.GetExtension(path).ToUpperInvariant() switch
        {
            ".PDF" => Pdf,
            ".PS" => PostScript,
            ".JPG" or ".JPEG" => Jpeg,
            ".PNG" => Png,
            ".TXT" => PlainText,
            _ => Unknown,
        };
    }
}

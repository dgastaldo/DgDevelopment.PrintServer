namespace PrintServer.Core.Printing;

public static class DocumentFormats
{
    public const string Pdf = "application/pdf";

    public const string PostScript = "application/postscript";

    public const string Jpeg = "image/jpeg";

    public const string Png = "image/png";

    public const string PlainText = "text/plain";

    public const string Rtf = "application/rtf";

    public const string Doc = "application/msword";

    public const string Docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    public const string Odt = "application/vnd.oasis.opendocument.text";

    public const string Xls = "application/vnd.ms-excel";

    public const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public const string Ods = "application/vnd.oasis.opendocument.spreadsheet";

    public const string Ppt = "application/vnd.ms-powerpoint";

    public const string Pptx = "application/vnd.openxmlformats-officedocument.presentationml.presentation";

    public const string Odp = "application/vnd.oasis.opendocument.presentation";

    public const string Unknown = "application/octet-stream";

    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = Pdf,
        [".ps"] = PostScript,
        [".jpg"] = Jpeg,
        [".jpeg"] = Jpeg,
        [".png"] = Png,
        [".txt"] = PlainText,
        [".rtf"] = Rtf,
        [".doc"] = Doc,
        [".docx"] = Docx,
        [".odt"] = Odt,
        [".xls"] = Xls,
        [".xlsx"] = Xlsx,
        [".ods"] = Ods,
        [".ppt"] = Ppt,
        [".pptx"] = Pptx,
        [".odp"] = Odp,
    };

    public static IReadOnlyList<string> Printable { get; } = [Pdf, PostScript];

    public static IReadOnlyList<string> Extensions { get; } = [.. ByExtension.Keys];

    public static string ForFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return ByExtension.GetValueOrDefault(Path.GetExtension(path), Unknown);
    }

    public static bool IsPrintable(string format) => Printable.Contains(format);

    public static bool IsImage(string format) => format is Jpeg or Png;
}

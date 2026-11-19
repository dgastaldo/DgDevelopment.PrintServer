namespace PrintServer.Watching;

internal sealed class FileFilter
{
    private static readonly string[] DefaultExtensions = [".pdf", ".ps"];

    private static readonly string[] TemporarySuffixes = [".tmp", ".part", ".partial", ".crdownload", ".download"];

    private readonly HashSet<string> _extensions;

    public FileFilter(WatcherOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _extensions = new HashSet<string>(
            (options.Extensions ?? DefaultExtensions).Select(NormalizeExtension),
            StringComparer.OrdinalIgnoreCase);
    }

    public bool Accepts(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        if (fileName.Length == 0 || fileName[0] is '.' or '~')
        {
            return false;
        }

        if (TemporarySuffixes.Any(suffix => fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return _extensions.Contains(Path.GetExtension(fileName));
    }

    private static string NormalizeExtension(string extension)
    {
        var trimmed = extension.Trim();
        return trimmed.StartsWith('.') ? trimmed : "." + trimmed;
    }
}

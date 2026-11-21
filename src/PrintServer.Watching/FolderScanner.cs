using PrintServer.Core.Printing;
using PrintServer.Core.Queue;

namespace PrintServer.Watching;

internal sealed class FolderScanner
{
    private const int MaxOptionDepth = 2;

    private readonly IFileSystem _fileSystem;
    private readonly FileFilter _filter;
    private readonly HashSet<string> _ignoredFolders;

    public FolderScanner(IFileSystem fileSystem, FileFilter filter, WatcherOptions options)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(options);

        _fileSystem = fileSystem;
        _filter = filter;
        _ignoredFolders = new HashSet<string>(options.IgnoredFolders ?? QueueFolders.All, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ScannedFile> Scan(string root)
    {
        var files = new List<ScannedFile>();
        if (!_fileSystem.DirectoryExists(root))
        {
            return files;
        }

        AddFiles(root, null, PrintOptions.Default, files);
        foreach (var directory in _fileSystem.GetDirectories(root))
        {
            var name = Path.GetFileName(directory);
            if (_ignoredFolders.Contains(name))
            {
                continue;
            }

            if (PrintOptionFolders.TryApply(name, PrintOptions.Default, out var applied))
            {
                AddFiles(directory, null, applied, files);
                ScanOptionFolders(directory, null, applied, 1, files);
            }
            else
            {
                AddFiles(directory, name, PrintOptions.Default, files);
                ScanOptionFolders(directory, name, PrintOptions.Default, 0, files);
            }
        }

        return files;
    }

    private void ScanOptionFolders(string directory, string? printerName, PrintOptions options, int depth, List<ScannedFile> files)
    {
        if (depth >= MaxOptionDepth)
        {
            return;
        }

        foreach (var sub in _fileSystem.GetDirectories(directory))
        {
            var name = Path.GetFileName(sub);
            if (_ignoredFolders.Contains(name) || !PrintOptionFolders.TryApply(name, options, out var applied))
            {
                continue;
            }

            AddFiles(sub, printerName, applied, files);
            ScanOptionFolders(sub, printerName, applied, depth + 1, files);
        }
    }

    private void AddFiles(string directory, string? printerName, PrintOptions options, List<ScannedFile> files)
    {
        foreach (var entry in _fileSystem.GetFiles(directory).Where(entry => _filter.Accepts(Path.GetFileName(entry.Path))))
        {
            files.Add(new ScannedFile(entry, printerName, options));
        }
    }
}

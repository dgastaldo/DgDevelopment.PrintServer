using PrintServer.Core.Queue;

namespace PrintServer.Watching;

internal sealed class FolderScanner
{
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

        AddFiles(root, null, files);
        foreach (var directory in _fileSystem.GetDirectories(root))
        {
            var name = Path.GetFileName(directory);
            if (!_ignoredFolders.Contains(name))
            {
                AddFiles(directory, name, files);
            }
        }

        return files;
    }

    private void AddFiles(string directory, string? printerName, List<ScannedFile> files)
    {
        foreach (var entry in _fileSystem.GetFiles(directory).Where(entry => _filter.Accepts(Path.GetFileName(entry.Path))))
        {
            files.Add(new ScannedFile(entry, printerName));
        }
    }
}

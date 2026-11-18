namespace PrintServer.Watching;

public sealed class FileSystemWatcherNotifier : IFolderChangeNotifier
{
    private const int BufferSize = 64 * 1024;

    public IDisposable Watch(string path, Action onChange)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(onChange);

        var watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = true,
            InternalBufferSize = BufferSize,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
        };

        watcher.Created += (_, _) => onChange();
        watcher.Changed += (_, _) => onChange();
        watcher.Deleted += (_, _) => onChange();
        watcher.Renamed += (_, _) => onChange();
        watcher.Error += (_, _) => onChange();

        try
        {
            watcher.EnableRaisingEvents = true;
        }
        catch
        {
            watcher.Dispose();
            throw;
        }

        return watcher;
    }
}

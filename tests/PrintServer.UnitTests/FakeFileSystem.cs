using PrintServer.Watching;

namespace PrintServer.UnitTests;

internal sealed class FakeFileSystem : IFileSystem
{
    private readonly HashSet<string> _directories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FileEntry> _files = new(StringComparer.Ordinal);

    public void AddDirectory(string path) => _directories.Add(path);

    public void SetFile(string path, long length, DateTimeOffset lastWriteUtc, DateTimeOffset? createdUtc = null)
    {
        _files[path] = new FileEntry(path, length, lastWriteUtc, createdUtc ?? lastWriteUtc);
        var parent = Path.GetDirectoryName(path);
        while (!string.IsNullOrEmpty(parent))
        {
            _directories.Add(parent);
            parent = Path.GetDirectoryName(parent);
        }
    }

    public void RemoveFile(string path) => _files.Remove(path);

    public bool DirectoryExists(string path) => _directories.Contains(path);

    public IReadOnlyList<string> GetDirectories(string path) =>
        [.. _directories.Where(directory => string.Equals(Path.GetDirectoryName(directory), path, StringComparison.Ordinal))];

    public IReadOnlyList<FileEntry> GetFiles(string path) =>
        [.. _files.Values.Where(file => string.Equals(Path.GetDirectoryName(file.Path), path, StringComparison.Ordinal))];
}

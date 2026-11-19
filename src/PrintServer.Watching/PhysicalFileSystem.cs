namespace PrintServer.Watching;

public sealed class PhysicalFileSystem : IFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public IReadOnlyList<string> GetDirectories(string path)
    {
        try
        {
            return [.. Directory.EnumerateDirectories(path)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public IReadOnlyList<FileEntry> GetFiles(string path)
    {
        var entries = new List<FileEntry>();
        try
        {
            foreach (var file in new DirectoryInfo(path).EnumerateFiles())
            {
                entries.Add(new FileEntry(file.FullName, file.Length, file.LastWriteTimeUtc, file.CreationTimeUtc));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return entries;
        }

        return entries;
    }
}

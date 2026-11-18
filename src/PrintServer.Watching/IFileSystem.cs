namespace PrintServer.Watching;

public interface IFileSystem
{
    bool DirectoryExists(string path);

    IReadOnlyList<string> GetDirectories(string path);

    IReadOnlyList<FileEntry> GetFiles(string path);
}

using PrintServer.Watching;

namespace PrintServer.UnitTests;

public sealed class PhysicalFileSystemTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"printserver-fs-{Guid.NewGuid():N}");
    private readonly PhysicalFileSystem _fileSystem = new();

    public PhysicalFileSystemTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ReportsExistingAndMissingDirectories()
    {
        Assert.True(_fileSystem.DirectoryExists(_root));
        Assert.False(_fileSystem.DirectoryExists(Path.Combine(_root, "missing")));
    }

    [Fact]
    public void ListsFilesWithSizeAndModificationTime()
    {
        var path = Path.Combine(_root, "a.pdf");
        File.WriteAllText(path, "12345");

        var entry = Assert.Single(_fileSystem.GetFiles(_root));

        Assert.Equal(path, entry.Path);
        Assert.Equal(5, entry.Length);
        Assert.Equal(File.GetLastWriteTimeUtc(path), entry.LastWriteUtc.UtcDateTime);
        Assert.Equal(File.GetCreationTimeUtc(path), entry.CreatedUtc.UtcDateTime);
    }

    [Fact]
    public void ListsSubdirectories()
    {
        var sub = Directory.CreateDirectory(Path.Combine(_root, "Office")).FullName;

        Assert.Equal([sub], _fileSystem.GetDirectories(_root));
    }

    [Fact]
    public void MissingDirectoriesYieldNothingInsteadOfThrowing()
    {
        var missing = Path.Combine(_root, "missing");

        Assert.Empty(_fileSystem.GetFiles(missing));
        Assert.Empty(_fileSystem.GetDirectories(missing));
    }
}

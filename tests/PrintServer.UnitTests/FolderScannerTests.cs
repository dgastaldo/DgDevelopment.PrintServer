using PrintServer.Watching;

namespace PrintServer.UnitTests;

public sealed class FolderScannerTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "printserver-scan-root");
    private readonly FakeFileSystem _fileSystem = new();

    private FolderScanner Create(Action<WatcherOptions>? configure = null)
    {
        var options = new WatcherOptions();
        configure?.Invoke(options);
        return new FolderScanner(_fileSystem, new FileFilter(options), options);
    }

    private string InRoot(params string[] parts) => Path.Combine([_root, .. parts]);

    [Fact]
    public void MissingRootYieldsNothing()
    {
        Assert.Empty(Create().Scan(_root));
    }

    [Fact]
    public void FilesInTheRootHaveNoPrinter()
    {
        _fileSystem.SetFile(InRoot("a.pdf"), 10, Stamp);

        var file = Assert.Single(Create().Scan(_root));

        Assert.Equal(InRoot("a.pdf"), file.Entry.Path);
        Assert.Null(file.PrinterName);
    }

    [Fact]
    public void FilesInASubfolderTakeTheFolderNameAsPrinter()
    {
        _fileSystem.SetFile(InRoot("Office", "a.pdf"), 10, Stamp);

        var file = Assert.Single(Create().Scan(_root));

        Assert.Equal("Office", file.PrinterName);
    }

    [Fact]
    public void ProcessedAndErrorFoldersAreIgnoredIncludingTheirCase()
    {
        _fileSystem.SetFile(InRoot("processati", "done.pdf"), 10, Stamp);
        _fileSystem.SetFile(InRoot("ERRORI", "bad.pdf"), 10, Stamp);

        Assert.Empty(Create().Scan(_root));
    }

    [Theory]
    [InlineData("processing")]
    [InlineData("stallo")]
    [InlineData("duplicati")]
    public void TheQueueFoldersAreIgnoredByDefault(string folder)
    {
        _fileSystem.SetFile(InRoot(folder, "a.pdf"), 10, Stamp);

        Assert.Empty(Create().Scan(_root));
    }

    [Fact]
    public void IgnoredFoldersCanBeConfigured()
    {
        _fileSystem.SetFile(InRoot("archive", "old.pdf"), 10, Stamp);
        _fileSystem.SetFile(InRoot("processati", "done.pdf"), 10, Stamp);

        var files = Create(options => options.IgnoredFolders = ["archive"]).Scan(_root);

        var file = Assert.Single(files);
        Assert.Equal("processati", file.PrinterName);
    }

    [Fact]
    public void OnlyOneLevelOfSubfoldersIsVisited()
    {
        _fileSystem.SetFile(InRoot("Office", "deep", "a.pdf"), 10, Stamp);

        Assert.Empty(Create().Scan(_root));
    }

    [Fact]
    public void FilteredFilesAreSkipped()
    {
        _fileSystem.SetFile(InRoot("a.pdf"), 10, Stamp);
        _fileSystem.SetFile(InRoot("b.pdf.tmp"), 10, Stamp);
        _fileSystem.SetFile(InRoot("c.docx"), 10, Stamp);

        var file = Assert.Single(Create().Scan(_root));

        Assert.Equal(InRoot("a.pdf"), file.Entry.Path);
    }
}

using PrintServer.Watching;

namespace PrintServer.UnitTests;

public sealed class FileStabilityTrackerTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);
    private static readonly DateTimeOffset Start = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly ManualTimeProvider _time = new(Start);
    private readonly FileStabilityTracker _tracker;

    public FileStabilityTrackerTests() => _tracker = new FileStabilityTracker(_time, Window);

    private static ScannedFile MakeFile(string name, long length, DateTimeOffset lastWrite, string? printer = null) =>
        new(new FileEntry(name, length, lastWrite, lastWrite), printer);

    [Fact]
    public void FirstSightingIsNeverReported()
    {
        var ready = _tracker.Update([MakeFile("a.pdf", 10, Start)]);

        Assert.Empty(ready);
        Assert.True(_tracker.HasPending);
    }

    [Fact]
    public void UnchangedFileIsReportedOnceAfterTheWindow()
    {
        var file = MakeFile("a.pdf", 10, Start, "Office");
        _tracker.Update([file]);

        _time.Advance(Window);
        var ready = _tracker.Update([file]);

        Assert.Same(file, Assert.Single(ready));
        Assert.False(_tracker.HasPending);

        _time.Advance(Window);
        Assert.Empty(_tracker.Update([file]));
    }

    [Fact]
    public void FileIsNotReportedBeforeTheWindowElapses()
    {
        var file = MakeFile("a.pdf", 10, Start);
        _tracker.Update([file]);

        _time.Advance(Window - TimeSpan.FromMilliseconds(1));

        Assert.Empty(_tracker.Update([file]));
    }

    [Fact]
    public void GrowingFileRestartsTheWindow()
    {
        _tracker.Update([MakeFile("a.pdf", 10, Start)]);

        _time.Advance(TimeSpan.FromSeconds(8));
        _tracker.Update([MakeFile("a.pdf", 20, Start.AddSeconds(8))]);

        _time.Advance(TimeSpan.FromSeconds(8));
        Assert.Empty(_tracker.Update([MakeFile("a.pdf", 20, Start.AddSeconds(8))]));

        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.Single(_tracker.Update([MakeFile("a.pdf", 20, Start.AddSeconds(8))]));
    }

    [Fact]
    public void ChangedModificationTimeAloneRestartsTheWindow()
    {
        _tracker.Update([MakeFile("a.pdf", 10, Start)]);

        _time.Advance(Window);
        Assert.Empty(_tracker.Update([MakeFile("a.pdf", 10, Start.AddSeconds(5))]));
    }

    [Fact]
    public void RewrittenFileIsReportedAgainAfterItSettles()
    {
        var first = MakeFile("a.pdf", 10, Start);
        _tracker.Update([first]);
        _time.Advance(Window);
        Assert.Single(_tracker.Update([first]));

        var second = MakeFile("a.pdf", 30, Start.AddSeconds(20));
        _tracker.Update([second]);
        _time.Advance(Window);

        Assert.Same(second, Assert.Single(_tracker.Update([second])));
    }

    [Fact]
    public void FileThatDisappearsBeforeSettlingIsDropped()
    {
        var file = MakeFile("a.pdf", 10, Start);
        _tracker.Update([file]);

        _time.Advance(Window);
        Assert.Empty(_tracker.Update([]));
        Assert.False(_tracker.HasPending);

        Assert.Empty(_tracker.Update([file]));
    }

    [Fact]
    public void FilesAreTrackedIndependently()
    {
        var a = MakeFile("a.pdf", 10, Start);
        var b = MakeFile("b.pdf", 10, Start.AddSeconds(6));
        _tracker.Update([a]);

        _time.Advance(TimeSpan.FromSeconds(6));
        _tracker.Update([a, b]);

        _time.Advance(TimeSpan.FromSeconds(4));
        Assert.Same(a, Assert.Single(_tracker.Update([a, b])));

        _time.Advance(TimeSpan.FromSeconds(6));
        Assert.Same(b, Assert.Single(_tracker.Update([a, b])));
    }

    [Fact]
    public void SeededFilesAreNotReportedUntilTheyChange()
    {
        var file = MakeFile("a.pdf", 10, Start);
        _tracker.Seed([file]);

        Assert.False(_tracker.HasPending);
        _time.Advance(Window);
        Assert.Empty(_tracker.Update([file]));

        var changed = MakeFile("a.pdf", 99, Start.AddSeconds(30));
        _tracker.Update([changed]);
        _time.Advance(Window);
        Assert.Single(_tracker.Update([changed]));
    }

    [Fact]
    public void NullArgumentsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => _tracker.Update(null!));
        Assert.Throws<ArgumentNullException>(() => _tracker.Seed(null!));
    }
}

using PrintServer.Watching;

namespace PrintServer.UnitTests;

public sealed class FileSystemWatcherNotifierTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"printserver-notifier-{Guid.NewGuid():N}");
    private readonly FileSystemWatcherNotifier _notifier = new();

    public FileSystemWatcherNotifierTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task ReportsChangesInsideTheFolderUntilDisposed()
    {
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (_notifier.Watch(_root, () => changed.TrySetResult()))
        {
            await File.WriteAllTextAsync(Path.Combine(_root, "a.pdf"), "content");

            await changed.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }

        var afterDispose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (_notifier.Watch(_root, () => afterDispose.TrySetResult()))
        {
            await File.WriteAllTextAsync(Path.Combine(_root, "b.pdf"), "content");
            await afterDispose.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }

        Assert.True(changed.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public void AMissingFolderIsRejected()
    {
        Assert.Throws<ArgumentException>(() => _notifier.Watch(Path.Combine(_root, "missing"), () => { }));
    }

    [Fact]
    public void ArgumentsAreValidated()
    {
        Assert.Throws<ArgumentException>(() => _notifier.Watch(" ", () => { }));
        Assert.Throws<ArgumentNullException>(() => _notifier.Watch(_root, null!));
    }
}

using PrintServer.Watching;

namespace PrintServer.UnitTests;

internal sealed class FakeFolderChangeNotifier : IFolderChangeNotifier
{
    private Action? _onChange;

    public int WatchCalls { get; private set; }

    public int DisposeCalls { get; private set; }

    public string? WatchedPath { get; private set; }

    public Exception? ThrowOnWatch { get; set; }

    public IDisposable Watch(string path, Action onChange)
    {
        WatchCalls++;
        if (ThrowOnWatch is not null)
        {
            throw ThrowOnWatch;
        }

        WatchedPath = path;
        _onChange = onChange;
        return new Subscription(this);
    }

    public void Raise() => _onChange?.Invoke();

    private sealed class Subscription(FakeFolderChangeNotifier owner) : IDisposable
    {
        public void Dispose() => owner.DisposeCalls++;
    }
}

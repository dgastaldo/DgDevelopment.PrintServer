using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PrintServer.Core.Watching;
using PrintServer.Watching;

namespace PrintServer.UnitTests;

public sealed class FolderWatcherServiceTests : IDisposable
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(1);
    private static readonly DateTimeOffset Start = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "printserver-service-root");
    private readonly ManualTimeProvider _time = new(Start);
    private readonly FakeFileSystem _fileSystem = new();
    private readonly FakeFolderChangeNotifier _notifier = new();
    private readonly List<FolderWatcherService> _services = [];

    public void Dispose()
    {
        foreach (var service in _services)
        {
            service.Dispose();
        }
    }

    private string InRoot(params string[] parts) => Path.Combine([_root, .. parts]);

    private (FolderWatcherService Service, Channel<DetectedFile> Channel) Create(Action<WatcherOptions>? configure = null, int capacity = 10)
    {
        var options = new WatcherOptions { Path = _root, StabilityWindow = Window, PollInterval = TimeSpan.FromMilliseconds(100) };
        configure?.Invoke(options);
        var channel = Channel.CreateBounded<DetectedFile>(capacity);
        var service = new FolderWatcherService(Options.Create(options), channel, _fileSystem, _notifier, _time, NullLogger<FolderWatcherService>.Instance);
        _services.Add(service);
        return (service, channel);
    }

    private static async Task<DetectedFile> ReadAsync(Channel<DetectedFile> channel)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await channel.Reader.ReadAsync(cts.Token);
    }

    private async Task<DetectedFile> ReadWhileAdvancingAsync(Channel<DetectedFile> channel)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            if (channel.Reader.TryRead(out var file))
            {
                return file;
            }

            _time.Advance(Window);
            await Task.Delay(50, cts.Token);
        }
    }

    [Fact]
    public async Task PublishesAFileOnceItHasSettledWithItsPrinter()
    {
        var (service, channel) = Create();
        _fileSystem.SetFile(InRoot("Office", "a.pdf"), 42, Start);

        await service.ScanOnceAsync(initial: true, CancellationToken.None);
        Assert.False(channel.Reader.TryRead(out _));

        _time.Advance(Window);
        await service.ScanOnceAsync(initial: false, CancellationToken.None);

        var file = await ReadAsync(channel);
        Assert.Equal(new DetectedFile(InRoot("Office", "a.pdf"), "Office", 42, Start), file);
    }

    [Fact]
    public async Task ThePublishedFileCarriesItsCreationTime()
    {
        var (service, channel) = Create();
        var created = Start.AddDays(-3);
        _fileSystem.SetFile(InRoot("a.pdf"), 10, Start, created);

        await service.ScanOnceAsync(initial: true, CancellationToken.None);
        _time.Advance(Window);
        await service.ScanOnceAsync(initial: false, CancellationToken.None);

        var file = await ReadAsync(channel);
        Assert.Equal(created, file.CreatedUtc);
    }

    [Fact]
    public async Task ExistingFilesArePublishedByDefault()
    {
        var (service, channel) = Create();
        _fileSystem.SetFile(InRoot("a.pdf"), 10, Start);

        await service.ScanOnceAsync(initial: true, CancellationToken.None);
        _time.Advance(Window);
        await service.ScanOnceAsync(initial: false, CancellationToken.None);

        var file = await ReadAsync(channel);
        Assert.Null(file.PrinterName);
    }

    [Fact]
    public async Task ExistingFilesCanBeIgnoredWhileNewOnesAreStillPublished()
    {
        var (service, channel) = Create(options => options.ProcessExistingFiles = false);
        _fileSystem.SetFile(InRoot("old.pdf"), 10, Start);

        await service.ScanOnceAsync(initial: true, CancellationToken.None);
        _fileSystem.SetFile(InRoot("new.pdf"), 20, Start);
        await service.ScanOnceAsync(initial: false, CancellationToken.None);
        _time.Advance(Window);
        await service.ScanOnceAsync(initial: false, CancellationToken.None);

        var file = await ReadAsync(channel);
        Assert.Equal(InRoot("new.pdf"), file.FilePath);
        Assert.False(channel.Reader.TryRead(out _));
    }

    [Fact]
    public async Task ASettledFileIsNotPublishedTwice()
    {
        var (service, channel) = Create();
        _fileSystem.SetFile(InRoot("a.pdf"), 10, Start);

        await service.ScanOnceAsync(initial: true, CancellationToken.None);
        _time.Advance(Window);
        await service.ScanOnceAsync(initial: false, CancellationToken.None);
        _time.Advance(Window);
        await service.ScanOnceAsync(initial: false, CancellationToken.None);

        Assert.True(channel.Reader.TryRead(out _));
        Assert.False(channel.Reader.TryRead(out _));
    }

    [Fact]
    public async Task AFileRemovedBeforeItSettlesIsNeverPublished()
    {
        var (service, channel) = Create();
        _fileSystem.SetFile(InRoot("a.pdf"), 10, Start);

        await service.ScanOnceAsync(initial: true, CancellationToken.None);
        _fileSystem.RemoveFile(InRoot("a.pdf"));
        _time.Advance(Window);
        await service.ScanOnceAsync(initial: false, CancellationToken.None);

        Assert.False(channel.Reader.TryRead(out _));
    }

    [Fact]
    public async Task AFullChannelMakesThePublisherWaitUntilThereIsRoom()
    {
        var (service, channel) = Create(capacity: 1);
        _fileSystem.SetFile(InRoot("a.pdf"), 10, Start);
        _fileSystem.SetFile(InRoot("b.pdf"), 10, Start);
        await service.ScanOnceAsync(initial: true, CancellationToken.None);
        _time.Advance(Window);

        var scan = service.ScanOnceAsync(initial: false, CancellationToken.None);
        Assert.False(scan.IsCompleted);

        Assert.True(channel.Reader.TryRead(out _));
        await scan.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(channel.Reader.TryRead(out _));
    }

    [Fact]
    public async Task CancellationInterruptsAPublisherWaitingForRoom()
    {
        var (service, _) = Create(capacity: 1);
        _fileSystem.SetFile(InRoot("a.pdf"), 10, Start);
        _fileSystem.SetFile(InRoot("b.pdf"), 10, Start);
        await service.ScanOnceAsync(initial: true, CancellationToken.None);
        _time.Advance(Window);

        using var cts = new CancellationTokenSource();
        var scan = service.ScanOnceAsync(initial: false, cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scan);
    }

    [Fact]
    public async Task WatchesTheRootAndPublishesWhenAnEventArrives()
    {
        var (service, channel) = Create();
        _fileSystem.AddDirectory(_root);
        await service.StartAsync(CancellationToken.None);

        _fileSystem.SetFile(InRoot("a.pdf"), 10, Start);
        _notifier.Raise();

        var file = await ReadWhileAdvancingAsync(channel);
        Assert.Equal(InRoot("a.pdf"), file.FilePath);
        Assert.Equal(1, _notifier.WatchCalls);
        Assert.Equal(_root, _notifier.WatchedPath);

        await service.StopAsync(CancellationToken.None);
        Assert.True(_notifier.DisposeCalls >= 1);
    }

    [Fact]
    public async Task FallsBackToPollingWhenEventsAreNotAvailable()
    {
        var (service, channel) = Create();
        _notifier.ThrowOnWatch = new IOException("inotify limit reached");
        _fileSystem.SetFile(InRoot("a.pdf"), 10, Start);
        await service.StartAsync(CancellationToken.None);

        var file = await ReadWhileAdvancingAsync(channel);
        Assert.Equal(InRoot("a.pdf"), file.FilePath);
        Assert.Equal(1, _notifier.WatchCalls);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task PollingOnlyModeNeverCreatesAWatcher()
    {
        var (service, channel) = Create(options => options.UsePolling = true);
        _fileSystem.SetFile(InRoot("a.pdf"), 10, Start);
        await service.StartAsync(CancellationToken.None);

        await ReadWhileAdvancingAsync(channel);
        Assert.Equal(0, _notifier.WatchCalls);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task WaitsForAMissingFolderAndStartsWatchingWhenItAppears()
    {
        var (service, channel) = Create();
        await service.StartAsync(CancellationToken.None);
        await Task.Delay(300);
        Assert.Equal(0, _notifier.WatchCalls);

        _fileSystem.SetFile(InRoot("a.pdf"), 10, Start);

        var file = await ReadWhileAdvancingAsync(channel);
        Assert.Equal(InRoot("a.pdf"), file.FilePath);
        Assert.Equal(1, _notifier.WatchCalls);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void ThePathIsRequired()
    {
        var channel = Channel.CreateBounded<DetectedFile>(1);

        Assert.Throws<InvalidOperationException>(() => new FolderWatcherService(
            Options.Create(new WatcherOptions()), channel, _fileSystem, _notifier, _time, NullLogger<FolderWatcherService>.Instance));
    }

    [Fact]
    public void NullDependenciesAreRejected()
    {
        var options = Options.Create(new WatcherOptions { Path = _root });
        var channel = Channel.CreateBounded<DetectedFile>(1);
        var logger = NullLogger<FolderWatcherService>.Instance;

        Assert.Throws<ArgumentNullException>(() => new FolderWatcherService(null!, channel, _fileSystem, _notifier, _time, logger));
        Assert.Throws<ArgumentNullException>(() => new FolderWatcherService(options, null!, _fileSystem, _notifier, _time, logger));
        Assert.Throws<ArgumentNullException>(() => new FolderWatcherService(options, channel, null!, _notifier, _time, logger));
        Assert.Throws<ArgumentNullException>(() => new FolderWatcherService(options, channel, _fileSystem, null!, _time, logger));
        Assert.Throws<ArgumentNullException>(() => new FolderWatcherService(options, channel, _fileSystem, _notifier, null!, logger));
        Assert.Throws<ArgumentNullException>(() => new FolderWatcherService(options, channel, _fileSystem, _notifier, _time, null!));
    }
}

using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PrintServer.Core.Watching;
using PrintServer.Watching;

namespace PrintServer.UnitTests;

public sealed class FolderWatcherIntegrationTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"printserver-watch-{Guid.NewGuid():N}");
    private readonly List<FolderWatcherService> _services = [];

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        foreach (var service in _services)
        {
            await service.StopAsync(CancellationToken.None);
            service.Dispose();
        }

        Directory.Delete(_root, recursive: true);
    }

    private async Task<Channel<DetectedFile>> StartAsync(Action<WatcherOptions>? configure = null)
    {
        var options = new WatcherOptions
        {
            Path = _root,
            StabilityWindow = TimeSpan.FromMilliseconds(300),
            PollInterval = TimeSpan.FromMilliseconds(100),
        };
        configure?.Invoke(options);

        var channel = Channel.CreateBounded<DetectedFile>(10);
        var service = new FolderWatcherService(
            Options.Create(options),
            channel,
            new PhysicalFileSystem(),
            new FileSystemWatcherNotifier(),
            TimeProvider.System,
            NullLogger<FolderWatcherService>.Instance);
        _services.Add(service);
        await service.StartAsync(CancellationToken.None);
        return channel;
    }

    private static async Task<DetectedFile> ReadAsync(Channel<DetectedFile> channel)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        return await channel.Reader.ReadAsync(cts.Token);
    }

    private static async Task AssertNothingMoreAsync(Channel<DetectedFile> channel)
    {
        await Task.Delay(900);
        Assert.False(channel.Reader.TryRead(out _));
    }

    [Fact]
    public async Task ADroppedFileIsPublishedOnceItIsComplete()
    {
        var channel = await StartAsync();
        var path = Path.Combine(_root, "a.pdf");

        await using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            await stream.WriteAsync(new byte[5]);
            await stream.FlushAsync();
            await Task.Delay(150);
            await stream.WriteAsync(new byte[5]);
        }

        var file = await ReadAsync(channel);

        Assert.Equal(path, file.FilePath);
        Assert.Equal(10, file.Length);
        Assert.Null(file.PrinterName);
        await AssertNothingMoreAsync(channel);
    }

    [Fact]
    public async Task AFileInANewSubfolderCarriesThePrinterName()
    {
        var channel = await StartAsync();

        Directory.CreateDirectory(Path.Combine(_root, "Office"));
        await File.WriteAllTextAsync(Path.Combine(_root, "Office", "a.pdf"), "content");

        var file = await ReadAsync(channel);

        Assert.Equal("Office", file.PrinterName);
    }

    [Fact]
    public async Task ATemporaryFileRenamedIntoPlaceIsPublishedUnderItsFinalName()
    {
        var channel = await StartAsync();
        var temporary = Path.Combine(_root, "a.pdf.tmp");
        var final = Path.Combine(_root, "a.pdf");

        await File.WriteAllTextAsync(temporary, "content");
        File.Move(temporary, final);

        var file = await ReadAsync(channel);

        Assert.Equal(final, file.FilePath);
        await AssertNothingMoreAsync(channel);
    }

    [Fact]
    public async Task FilesAlreadyThereAtStartupArePublished()
    {
        var path = Path.Combine(_root, "existing.pdf");
        await File.WriteAllTextAsync(path, "content");

        var channel = await StartAsync();

        var file = await ReadAsync(channel);
        Assert.Equal(path, file.FilePath);
    }

    [Fact]
    public async Task FilesAlreadyThereAtStartupCanBeIgnored()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "existing.pdf"), "content");

        var channel = await StartAsync(options => options.ProcessExistingFiles = false);

        await AssertNothingMoreAsync(channel);
    }

    [Fact]
    public async Task PollingOnlyModeFindsNewFiles()
    {
        var channel = await StartAsync(options => options.UsePolling = true);
        var path = Path.Combine(_root, "a.pdf");

        await File.WriteAllTextAsync(path, "content");

        var file = await ReadAsync(channel);
        Assert.Equal(path, file.FilePath);
    }

    [Fact]
    public async Task FilesInTheProcessedFolderAreIgnored()
    {
        var channel = await StartAsync();
        Directory.CreateDirectory(Path.Combine(_root, "processed"));
        await File.WriteAllTextAsync(Path.Combine(_root, "processed", "done.pdf"), "content");
        var path = Path.Combine(_root, "new.pdf");
        await File.WriteAllTextAsync(path, "content");

        var file = await ReadAsync(channel);

        Assert.Equal(path, file.FilePath);
        await AssertNothingMoreAsync(channel);
    }
}

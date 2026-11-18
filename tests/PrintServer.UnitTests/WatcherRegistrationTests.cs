using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PrintServer.Core.Watching;
using PrintServer.Watching;

namespace PrintServer.UnitTests;

public sealed class WatcherRegistrationTests
{
    private static IConfiguration Configure(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();

    private static ServiceProvider Build(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFolderWatcher(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void NothingIsRegisteredWithoutAPath()
    {
        using var provider = Build(Configure(("Watcher:StabilityWindow", "00:00:05")));

        Assert.Empty(provider.GetServices<IHostedService>());
        Assert.Null(provider.GetService<Channel<DetectedFile>>());
    }

    [Fact]
    public void ABlankPathCountsAsNotConfigured()
    {
        using var provider = Build(Configure(("Watcher:Path", "  ")));

        Assert.Empty(provider.GetServices<IHostedService>());
    }

    [Fact]
    public void ThePathRegistersTheServiceAndABoundedChannel()
    {
        using var provider = Build(Configure(("Watcher:Path", Path.GetTempPath()), ("Watcher:QueueCapacity", "2")));

        Assert.Single(provider.GetServices<IHostedService>().OfType<FolderWatcherService>());
        var channel = provider.GetRequiredService<Channel<DetectedFile>>();
        Assert.True(channel.Writer.TryWrite(new DetectedFile("a", null, 1)));
        Assert.True(channel.Writer.TryWrite(new DetectedFile("b", null, 1)));
        Assert.False(channel.Writer.TryWrite(new DetectedFile("c", null, 1)));
    }

    [Fact]
    public void OptionsAreBoundFromConfiguration()
    {
        using var provider = Build(Configure(
            ("Watcher:Path", Path.GetTempPath()),
            ("Watcher:Extensions:0", ".pdf"),
            ("Watcher:Extensions:1", ".ps"),
            ("Watcher:Extensions:2", ".png"),
            ("Watcher:IgnoredFolders:0", "archive"),
            ("Watcher:StabilityWindow", "00:00:05"),
            ("Watcher:UsePolling", "true"),
            ("Watcher:PollInterval", "00:01:00"),
            ("Watcher:ProcessExistingFiles", "false")));

        var options = provider.GetRequiredService<IOptions<WatcherOptions>>().Value;

        Assert.Equal([".pdf", ".ps", ".png"], options.Extensions);
        Assert.Equal(["archive"], options.IgnoredFolders);
        Assert.Equal(TimeSpan.FromSeconds(5), options.StabilityWindow);
        Assert.True(options.UsePolling);
        Assert.Equal(TimeSpan.FromMinutes(1), options.PollInterval);
        Assert.False(options.ProcessExistingFiles);
    }

    [Fact]
    public void DefaultsMatchTheDocumentedBehaviour()
    {
        using var provider = Build(Configure(("Watcher:Path", Path.GetTempPath())));

        var options = provider.GetRequiredService<IOptions<WatcherOptions>>().Value;

        Assert.Null(options.Extensions);
        Assert.Null(options.IgnoredFolders);
        Assert.Equal(TimeSpan.FromSeconds(10), options.StabilityWindow);
        Assert.False(options.UsePolling);
        Assert.Equal(TimeSpan.FromSeconds(30), options.PollInterval);
        Assert.True(options.ProcessExistingFiles);
        Assert.Equal(256, options.QueueCapacity);
    }

    [Theory]
    [InlineData("Watcher:StabilityWindow", "00:00:00")]
    [InlineData("Watcher:PollInterval", "-00:00:01")]
    [InlineData("Watcher:QueueCapacity", "0")]
    public void InvalidValuesAreRejected(string key, string value)
    {
        using var provider = Build(Configure(("Watcher:Path", Path.GetTempPath()), (key, value)));

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<WatcherOptions>>().Value);
    }

    [Fact]
    public void ArgumentsAreValidated()
    {
        Assert.Throws<ArgumentNullException>(() => WatcherServiceCollectionExtensions.AddFolderWatcher(null!, Configure()));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddFolderWatcher(null!));
    }
}

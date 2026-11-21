using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PrintServer.Core.Watching;

namespace PrintServer.Watching;

public static class WatcherServiceCollectionExtensions
{
    public static IServiceCollection AddFolderWatcher(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(WatcherOptions.SectionName);
        if (string.IsNullOrWhiteSpace(section[nameof(WatcherOptions.Path)]))
        {
            return services;
        }

        services.AddOptions<WatcherOptions>()
            .Bind(section)
            .Validate(IsValid, "Watcher options are invalid: Path is required, StabilityWindow and PollInterval must be positive and QueueCapacity must be at least 1.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IFileSystem, PhysicalFileSystem>();
        services.TryAddSingleton<IFolderChangeNotifier, FileSystemWatcherNotifier>();
        services.TryAddSingleton(provider => Channel.CreateBounded<DetectedFile>(
            new BoundedChannelOptions(provider.GetRequiredService<IOptions<WatcherOptions>>().Value.QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = true,
            }));
        services.TryAddSingleton<IJobSubmitter, FolderJobSubmitter>();
        services.AddHostedService<FolderWatcherService>();

        return services;
    }

    private static bool IsValid(WatcherOptions options) =>
        !string.IsNullOrWhiteSpace(options.Path)
        && options.StabilityWindow > TimeSpan.Zero
        && options.PollInterval > TimeSpan.Zero
        && options.QueueCapacity > 0;
}

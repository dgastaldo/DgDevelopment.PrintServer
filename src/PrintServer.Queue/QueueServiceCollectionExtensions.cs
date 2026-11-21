using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PrintServer.Core.Queue;

namespace PrintServer.Queue;

public static class QueueServiceCollectionExtensions
{
    private const string WatcherPathKey = "Watcher:Path";
    private const int BusyTimeoutSeconds = 30;
    private const int MaxAttemptsLimit = 20;

    public static IServiceCollection AddJobQueue(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (string.IsNullOrWhiteSpace(configuration[WatcherPathKey]))
        {
            return services;
        }

        services.AddOptions<QueueOptions>()
            .Bind(configuration.GetSection(QueueOptions.SectionName))
            .Validate(options => options.StallAfter > TimeSpan.Zero, "Queue options are invalid: StallAfter must be positive.")
            .Validate(options => options.MaxAttempts is >= 1 and <= MaxAttemptsLimit, "Queue options are invalid: MaxAttempts must be between 1 and 20.")
            .Validate(options => options.RetryBaseDelay > TimeSpan.Zero, "Queue options are invalid: RetryBaseDelay must be positive.")
            .Validate(options => options.PollInterval > TimeSpan.Zero, "Queue options are invalid: PollInterval must be positive.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<QueueOptions>>().Value;
            var path = DatabaseLocator.Resolve(options.DatabasePath, DatabaseLocator.CurrentPlatform(), DatabaseLocator.CanWrite);
            return new QueueDatabaseLocation(path);
        });
        services.AddDbContextFactory<QueueDbContext>((provider, builder) =>
        {
            var location = provider.GetRequiredService<QueueDatabaseLocation>();
            var connection = new SqliteConnectionStringBuilder
            {
                DataSource = location.Path,
                DefaultTimeout = BusyTimeoutSeconds,
            };
            builder.UseSqlite(connection.ToString());
        });
        services.TryAddSingleton<IJobStore, SqliteJobStore>();
        services.TryAddSingleton<IJobFiles, PhysicalJobFiles>();
        services.TryAddSingleton<StallEvaluator>();
        services.TryAddSingleton<JobIntake>();
        services.TryAddSingleton<JobRecovery>();
        services.TryAddSingleton<JobAdministrator>();
        services.TryAddSingleton<RetryPolicy>();
        services.TryAddSingleton<JobProcessor>();
        services.AddHostedService<QueueInitializer>();
        services.AddHostedService<JobIntakeService>();
        services.AddHostedService<JobProcessingService>();

        return services;
    }
}

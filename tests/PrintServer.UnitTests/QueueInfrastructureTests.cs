using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PrintServer.Core.Printing;
using PrintServer.Core.Queue;
using PrintServer.Queue;

namespace PrintServer.UnitTests;

public sealed class QueueInfrastructureTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"printserver-queue-infra-{Guid.NewGuid():N}");

    public QueueInfrastructureTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }

    private static IConfiguration Configure(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();

    private static ServiceProvider Build(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddJobQueue(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AConfiguredDatabasePathWins()
    {
        var path = DatabaseLocator.Resolve(Path.Combine(_directory, "custom.db"), QueuePlatform.Linux, _ => throw new InvalidOperationException("not needed"));

        Assert.Equal(Path.Combine(_directory, "custom.db"), path);
    }

    [Fact]
    public void ARelativeDatabasePathIsMadeAbsolute()
    {
        var path = DatabaseLocator.Resolve("data/queue.db", QueuePlatform.Linux, _ => true);

        Assert.True(Path.IsPathRooted(path));
        Assert.EndsWith("queue.db", path, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(QueuePlatform.Linux, "/var/lib/printserver")]
    [InlineData(QueuePlatform.MacOS, "/Library/Application Support/PrintServer")]
    public void TheMachineWideFolderIsUsedWhenItIsWritable(QueuePlatform platform, string expected)
    {
        var path = DatabaseLocator.Resolve(null, platform, directory => directory == expected);

        Assert.Equal(Path.Combine(expected, "queue.db"), path);
    }

    [Fact]
    public void WindowsUsesProgramData()
    {
        var directory = DatabaseLocator.MachineWideDirectory(QueuePlatform.Windows);

        Assert.EndsWith("PrintServer", directory, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(directory, "queue.db"), DatabaseLocator.Resolve(null, QueuePlatform.Windows, _ => true));
    }

    [Theory]
    [InlineData(QueuePlatform.Windows)]
    [InlineData(QueuePlatform.MacOS)]
    [InlineData(QueuePlatform.Linux)]
    public void TheUserFolderIsTheFallbackWhenTheMachineWideOneIsNotWritable(QueuePlatform platform)
    {
        var path = DatabaseLocator.Resolve(null, platform, _ => false);

        Assert.Equal(Path.Combine(DatabaseLocator.UserDirectory(), "queue.db"), path);
    }

    [Fact]
    public void TheCurrentPlatformMatchesTheOperatingSystem()
    {
        var expected = OperatingSystem.IsWindows() ? QueuePlatform.Windows
            : OperatingSystem.IsMacOS() ? QueuePlatform.MacOS
            : QueuePlatform.Linux;

        Assert.Equal(expected, DatabaseLocator.CurrentPlatform());
    }

    [Fact]
    public void AFolderIsWritableOnlyWhenItCanBeCreatedAndWrittenTo()
    {
        Assert.True(DatabaseLocator.CanWrite(Path.Combine(_directory, "new", "folder")));

        var file = Path.Combine(_directory, "afile");
        File.WriteAllText(file, "x");
        Assert.False(DatabaseLocator.CanWrite(Path.Combine(file, "sub")));
    }

    [Fact]
    public void ResolveRejectsAMissingProbe()
    {
        Assert.Throws<ArgumentNullException>(() => DatabaseLocator.Resolve(null, QueuePlatform.Linux, null!));
    }

    [Theory]
    [InlineData(JobState.Pending, "in_attesa")]
    [InlineData(JobState.Converting, "in_conversione")]
    [InlineData(JobState.Printing, "in_stampa")]
    [InlineData(JobState.Completed, "completato")]
    [InlineData(JobState.Failed, "fallito")]
    [InlineData(JobState.Stalled, "in_stallo")]
    public void EveryStateHasAReadableDatabaseName(JobState state, string name)
    {
        Assert.Equal(name, JobStateNames.ToDatabase(state));
        Assert.Equal(state, JobStateNames.FromDatabase(name));
    }

    [Fact]
    public void UnknownStatesAreRejectedInBothDirections()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => JobStateNames.ToDatabase((JobState)99));
        Assert.Throws<InvalidOperationException>(() => JobStateNames.FromDatabase("sconosciuto"));
    }

    [Fact]
    public void PrintOptionsRoundTripThroughJson()
    {
        var options = new PrintOptions(PageOrientation.Portrait, DuplexMode.OneSided, 5);

        Assert.Equal(options, JobMapping.DeserializeOptions(JobMapping.SerializeOptions(options)));
        Assert.Equal(PrintOptions.Default, JobMapping.DeserializeOptions("null"));
        Assert.Equal(PrintOptions.Default, JobMapping.DeserializeOptions("{}"));
    }

    [Fact]
    public void OptionsAddedLaterDoNotBreakOlderRows()
    {
        var options = JobMapping.DeserializeOptions("""{"orientation":"landscape","paperSize":"a3"}""");

        Assert.Equal(PageOrientation.Landscape, options.Orientation);
        Assert.Equal(DuplexMode.Default, options.Duplex);
    }

    [Fact]
    public void MappingRejectsNulls()
    {
        Assert.Throws<ArgumentNullException>(() => JobMapping.ToEntity(null!, SqliteQueueFixture.Start));
        Assert.Throws<ArgumentNullException>(() => JobMapping.ToJob(null!));
    }

    [Fact]
    public void TheMigrationCreatesTheJobsTableAndItsIndexes()
    {
        using var queue = new SqliteQueueFixture();
        using var connection = new SqliteConnection($"Data Source={queue.DatabasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE name IN ('jobs', 'ux_jobs_active_file', 'ix_jobs_origin_file', 'ix_jobs_state_next') ORDER BY name";
        var names = new List<string>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                names.Add(reader.GetString(0));
            }
        }

        Assert.Equal(["ix_jobs_origin_file", "ix_jobs_state_next", "jobs", "ux_jobs_active_file"], names);
    }

    [Fact]
    public void NothingIsRegisteredWithoutTheWatcher()
    {
        using var provider = Build(Configure(("Queue:StallAfter", "02:00:00")));

        Assert.Null(provider.GetService<IJobStore>());
        Assert.Empty(provider.GetServices<IHostedService>());
    }

    [Fact]
    public void TheStoreIsRegisteredWhenTheWatcherIsConfigured()
    {
        using var provider = Build(Configure(("Watcher:Path", _directory), ("Queue:DatabasePath", Path.Combine(_directory, "q.db"))));

        Assert.IsType<SqliteJobStore>(provider.GetRequiredService<IJobStore>());
        Assert.Single(provider.GetServices<IHostedService>().OfType<QueueInitializer>());
        Assert.Equal(Path.Combine(_directory, "q.db"), provider.GetRequiredService<QueueDatabaseLocation>().Path);
    }

    [Fact]
    public void OptionsHaveDocumentedDefaultsAndAreBound()
    {
        using var defaults = Build(Configure(("Watcher:Path", _directory)));
        using var custom = Build(Configure(("Watcher:Path", _directory), ("Queue:StallAfter", "02:30:00")));

        Assert.Equal(TimeSpan.FromHours(1), defaults.GetRequiredService<IOptions<QueueOptions>>().Value.StallAfter);
        Assert.Null(defaults.GetRequiredService<IOptions<QueueOptions>>().Value.DatabasePath);
        Assert.Equal(TimeSpan.FromMinutes(150), custom.GetRequiredService<IOptions<QueueOptions>>().Value.StallAfter);
    }

    [Fact]
    public void ANonPositiveStallTimeIsRejected()
    {
        using var provider = Build(Configure(("Watcher:Path", _directory), ("Queue:StallAfter", "00:00:00")));

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<QueueOptions>>().Value);
    }

    [Fact]
    public async Task TheInitializerCreatesTheDatabaseFolderAndAppliesTheMigration()
    {
        var path = Path.Combine(_directory, "nested", "folder", "q.db");
        using var provider = Build(Configure(("Watcher:Path", _directory), ("Queue:DatabasePath", path)));
        var initializer = provider.GetServices<IHostedService>().OfType<QueueInitializer>().Single();

        await initializer.StartAsync(CancellationToken.None);
        await initializer.StopAsync(CancellationToken.None);

        Assert.True(File.Exists(path));
        var store = provider.GetRequiredService<IJobStore>();
        Assert.True(await store.TryEnqueueAsync(SqliteQueueFixture.MakeJob(), CancellationToken.None));
    }

    [Fact]
    public void RegistrationArgumentsAreValidated()
    {
        Assert.Throws<ArgumentNullException>(() => QueueServiceCollectionExtensions.AddJobQueue(null!, Configure()));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddJobQueue(null!));
    }
}

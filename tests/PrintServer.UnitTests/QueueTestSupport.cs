using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrintServer.Core.Printing;
using PrintServer.Core.Queue;
using PrintServer.Queue;

namespace PrintServer.UnitTests;

internal sealed class SqliteQueueFixture : IDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"printserver-queue-{Guid.NewGuid():N}");
    private readonly ServiceProvider _provider;

    public SqliteQueueFixture()
    {
        Directory.CreateDirectory(_directory);
        DatabasePath = Path.Combine(_directory, "queue.db");
        Time = new ManualTimeProvider(Start);

        var services = new ServiceCollection();
        services.AddDbContextFactory<QueueDbContext>(options => options.UseSqlite($"Data Source={DatabasePath}"));
        _provider = services.BuildServiceProvider();
        Factory = _provider.GetRequiredService<IDbContextFactory<QueueDbContext>>();
        using (var db = Factory.CreateDbContext())
        {
            db.Database.Migrate();
        }

        Store = new SqliteJobStore(Factory, Time);
    }

    public string DatabasePath { get; }

    public ManualTimeProvider Time { get; }

    public IDbContextFactory<QueueDbContext> Factory { get; }

    public SqliteJobStore Store { get; }

    public static NewJob MakeJob(
        string name = "a.pdf",
        string? printer = "Office",
        string hash = "hash-1",
        DateTimeOffset? created = null,
        string origin = "/inbox/Office",
        PrintOptions? options = null)
    {
        var id = Guid.NewGuid();
        return new NewJob(
            id,
            origin,
            name,
            Path.Combine(origin, name),
            Path.Combine(origin, "processing", id.ToString("N"), name),
            printer,
            created ?? Start.AddHours(-1),
            hash,
            1234,
            DocumentFormats.ForFile(name),
            options ?? PrintOptions.Default);
    }

    public void SetState(Guid id, JobState state)
    {
        using var db = Factory.CreateDbContext();
        db.Jobs.Where(job => job.Id == id).ExecuteUpdate(set => set.SetProperty(job => job.State, state));
    }

    public async Task<Job> EnqueueAsync(NewJob job)
    {
        Assert.True(await Store.TryEnqueueAsync(job, CancellationToken.None));
        return (await Store.GetAsync(job.Id, CancellationToken.None))!;
    }

    public void Dispose()
    {
        _provider.Dispose();
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }
}

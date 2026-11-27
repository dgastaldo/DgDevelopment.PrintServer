using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PrintServer.Core.Queue;
using PrintServer.Queue;

namespace PrintServer.UnitTests;

public sealed class QueueMigrationTests : IDisposable
{
    private const string InitialMigration = "20261119135017_InitialCreate";
    private const string EnglishMigration = "20261127120000_EnglishStateNames";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"printserver-migration-{Guid.NewGuid():N}");

    public QueueMigrationTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }

    private QueueDbContext Open()
    {
        var options = new DbContextOptionsBuilder<QueueDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_directory, "queue.db")}")
            .Options;
        return new QueueDbContext(options);
    }

    private static async Task InsertAsync(QueueDbContext db, string state, int number, string? fileName = null)
    {
        var id = $"00000000-0000-0000-0000-{number:D12}";
        var name = fileName ?? $"file{number}.pdf";
        var path = $"/inbox/{name}";
        const string options = "{}";
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO jobs (id, origin_directory, file_name, source_path, document_path, file_created_at, file_hash, file_size,
                source_format, document_format, options, state, attempts, created_at)
            VALUES ({id}, '/inbox', {name}, {path}, {path}, 1, 'hash', 1, '.pdf', '.pdf', {options}, {state}, 0, 1)
            """);
    }

    private static async Task<List<string>> StatesAsync(QueueDbContext db) =>
        await db.Database.SqlQueryRaw<string>("SELECT state AS Value FROM jobs ORDER BY file_name").ToListAsync();

    [Fact]
    public async Task ItalianStateNamesAreRenamedInExistingDatabases()
    {
        await using var db = Open();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(InitialMigration);
        var italian = new[] { "in_attesa", "in_conversione", "in_stampa", "completato", "fallito", "in_stallo", "annullato" };
        for (var i = 0; i < italian.Length; i++)
        {
            await InsertAsync(db, italian[i], i);
        }

        await migrator.MigrateAsync();

        Assert.Equal(["pending", "converting", "printing", "completed", "failed", "stalled", "cancelled"], await StatesAsync(db));
        var jobs = await db.Jobs.OrderBy(j => j.FileName).ToListAsync();
        Assert.Equal(
            [JobState.Pending, JobState.Converting, JobState.Printing, JobState.Completed, JobState.Failed, JobState.Stalled, JobState.Cancelled],
            jobs.Select(j => j.State));
    }

    [Fact]
    public async Task TheActiveFileIndexUsesTheEnglishNamesAfterTheMigration()
    {
        await using var db = Open();
        await db.GetService<IMigrator>().MigrateAsync();
        await InsertAsync(db, "pending", 1, "same.pdf");

        // Same origin, name, creation date and hash as an active job: the unique index must reject it.
        await Assert.ThrowsAsync<SqliteException>(() => InsertAsync(db, "printing", 2, "same.pdf"));

        // A finished job with the same identity is allowed.
        await InsertAsync(db, "completed", 3, "same.pdf");
    }

    [Fact]
    public async Task TheMigrationCanBeUndone()
    {
        await using var db = Open();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(EnglishMigration);
        await InsertAsync(db, "failed", 1);
        await InsertAsync(db, "pending", 2);

        await migrator.MigrateAsync(InitialMigration);

        Assert.Equal(["fallito", "in_attesa"], await StatesAsync(db));
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PrintServer.Queue;

public sealed partial class QueueInitializer(
    IDbContextFactory<QueueDbContext> factory,
    QueueDatabaseLocation location,
    JobRecovery recovery,
    ILogger<QueueInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(location.Path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var db = factory.CreateDbContext();
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
        LogReady(location.Path);

        await recovery.RecoverAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Job queue database ready at '{Path}'.")]
    private partial void LogReady(string path);
}

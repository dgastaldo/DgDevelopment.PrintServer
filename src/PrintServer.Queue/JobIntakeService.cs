using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PrintServer.Core.Watching;

namespace PrintServer.Queue;

public sealed partial class JobIntakeService(
    Channel<DetectedFile> channel,
    JobIntake intake,
    ILogger<JobIntakeService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var file in channel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await intake.IntakeAsync(file, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is DbUpdateException or SqliteException or IOException or InvalidOperationException)
            {
                LogFailed(ex, file.FilePath);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not queue '{Path}'.")]
    private partial void LogFailed(Exception exception, string path);
}

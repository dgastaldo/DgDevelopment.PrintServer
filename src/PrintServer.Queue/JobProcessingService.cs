using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PrintServer.Queue;

public sealed partial class JobProcessingService(
    JobProcessor processor,
    IOptions<QueueOptions> options,
    TimeProvider time,
    ILogger<JobProcessingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.PollInterval, time);
        do
        {
            try
            {
                await processor.RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is DbUpdateException or SqliteException or IOException or InvalidOperationException)
            {
                LogFailed(ex);
            }
        }
        while (await WaitAsync(timer, stoppingToken).ConfigureAwait(false));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The print worker could not process the queue; it will try again.")]
    private partial void LogFailed(Exception exception);
}

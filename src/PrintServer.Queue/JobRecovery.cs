using System.Net.Http;
using Microsoft.Extensions.Logging;
using PrintServer.Core.Printing;
using PrintServer.Core.Queue;

namespace PrintServer.Queue;

public sealed record RecoveryReport(int Requeued, int Completed, int Failed, int Resumed, int Reconciled, int Lost);

public sealed partial class JobRecovery
{
    private readonly IPrinterBackend _backend;
    private readonly IJobStore _store;
    private readonly IJobFiles _files;
    private readonly JobFileMover _mover;
    private readonly ILogger<JobRecovery> _logger;

    public JobRecovery(IPrinterBackend backend, IJobStore store, IJobFiles files, ILogger<JobRecovery> logger)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(logger);

        _backend = backend;
        _store = store;
        _files = files;
        _mover = new JobFileMover(files, store);
        _logger = logger;
    }

    public async Task<RecoveryReport> RecoverAsync(CancellationToken ct)
    {
        var requeued = 0;
        var completed = 0;
        var failed = 0;
        var resumed = 0;
        var reconciled = 0;
        var lost = 0;

        foreach (var job in await _store.ListAsync(JobState.Converting, ct).ConfigureAwait(false))
        {
            requeued += await _store.RequeueAsync(job.Id, ct).ConfigureAwait(false) ? 1 : 0;
        }

        foreach (var job in await _store.ListAsync(JobState.Printing, ct).ConfigureAwait(false))
        {
            switch (await RecoverPrintingAsync(job, ct).ConfigureAwait(false))
            {
                case JobStatus.Completed:
                    completed++;
                    break;
                case JobStatus.Error:
                    failed++;
                    break;
                case JobStatus.Queued:
                    requeued++;
                    break;
                default:
                    resumed++;
                    break;
            }
        }

        foreach (var job in await _store.ListAsync(JobState.Pending, ct).ConfigureAwait(false))
        {
            if (_files.Exists(job.DocumentPath))
            {
                continue;
            }

            if (await ReconcileAsync(job, ct).ConfigureAwait(false))
            {
                reconciled++;
            }
            else
            {
                lost++;
            }
        }

        var report = new RecoveryReport(requeued, completed, failed, resumed, reconciled, lost);
        LogRecovered(report.Requeued, report.Completed, report.Failed, report.Resumed, report.Reconciled, report.Lost);
        return report;
    }

    private async Task<JobStatus> RecoverPrintingAsync(Job job, CancellationToken ct)
    {
        if (job.BackendJobId is null)
        {
            await _store.RequeueAsync(job.Id, ct).ConfigureAwait(false);
            return JobStatus.Queued;
        }

        JobStatus status;
        try
        {
            status = await _backend.GetJobStatusAsync(job.BackendJobId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or HttpRequestException or TimeoutException)
        {
            LogStatusUnavailable(ex, job.Id);
            return JobStatus.Printing;
        }

        switch (status)
        {
            case JobStatus.Completed:
                await _store.CompleteAsync(job.Id, ct).ConfigureAwait(false);
                await _mover.MoveDocumentAsync(job, QueueFolders.Completed, ct).ConfigureAwait(false);
                break;
            case JobStatus.Error:
                await _store.FailAsync(job.Id, "The printer reported an error for the job.", ct).ConfigureAwait(false);
                await _mover.MoveDocumentAsync(job, QueueFolders.Failed, ct).ConfigureAwait(false);
                break;
            default:
                return JobStatus.Printing;
        }

        return status;
    }

    private async Task<bool> ReconcileAsync(Job job, CancellationToken ct)
    {
        if (_files.Exists(job.SourcePath))
        {
            try
            {
                _files.CreateDirectory(Path.GetDirectoryName(job.DocumentPath)!);
                _files.Move(job.SourcePath, job.DocumentPath);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogReconcileFailed(ex, job.Id);
            }
        }

        await _store.FailAsync(job.Id, "The file of the job is missing.", ct).ConfigureAwait(false);
        return false;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Queue recovery: {Requeued} requeued, {Completed} completed, {Failed} failed, {Resumed} still printing, {Reconciled} files reconciled, {Lost} lost.")]
    private partial void LogRecovered(int requeued, int completed, int failed, int resumed, int reconciled, int lost);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not ask the backend about job {JobId}; leaving it as printing.")]
    private partial void LogStatusUnavailable(Exception exception, Guid jobId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not move the file of job {JobId} into place.")]
    private partial void LogReconcileFailed(Exception exception, Guid jobId);
}

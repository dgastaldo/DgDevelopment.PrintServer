using System.Net.Http;
using Microsoft.Extensions.Logging;
using PrintServer.Core.Printing;
using PrintServer.Core.Queue;

namespace PrintServer.Queue;

public sealed partial class JobAdministrator
{
    private const string CancelReason = "Cancelled by the user.";

    private readonly IJobStore _store;
    private readonly IJobFiles _files;
    private readonly IPrinterBackend _backend;
    private readonly JobFileMover _mover;
    private readonly ILogger<JobAdministrator> _logger;

    public JobAdministrator(IJobStore store, IJobFiles files, IPrinterBackend backend, ILogger<JobAdministrator> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _files = files;
        _backend = backend;
        _mover = new JobFileMover(files, store);
        _logger = logger;
    }

    public async Task<QueueActionResult> RetryAsync(Guid id, CancellationToken ct)
    {
        var job = await _store.GetAsync(id, ct).ConfigureAwait(false);
        if (job is null)
        {
            return QueueActionResult.Failure("The job does not exist.");
        }

        if (job.State is not (JobState.Failed or JobState.Stalled or JobState.Cancelled))
        {
            return QueueActionResult.Failure("Only failed, stalled or cancelled jobs can be retried.");
        }

        if (!_files.Exists(job.DocumentPath))
        {
            return QueueActionResult.Failure("The file of the job is gone: put it again in the watched folder.");
        }

        switch (await _store.RestartAsync(id, ct).ConfigureAwait(false))
        {
            case RestartOutcome.Conflict:
                return QueueActionResult.Failure("Another job for the same file is already in the queue.");
            case RestartOutcome.NotRestartable:
                return QueueActionResult.Failure("The state of the job has just changed.");
        }

        var target = Path.Combine(job.OriginDirectory, QueueFolders.Processing, job.Id.ToString("N"), job.FileName);
        if (string.Equals(target, job.DocumentPath, StringComparison.Ordinal))
        {
            LogRetried(job.Id, job.FileName);
            return QueueActionResult.Success();
        }

        try
        {
            _files.CreateDirectory(Path.GetDirectoryName(target)!);
            _files.Move(job.DocumentPath, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _store.FailAsync(id, $"Could not move the file back for the retry: {ex.Message}", ct).ConfigureAwait(false);
            return QueueActionResult.Failure($"Could not move the file back: {ex.Message}");
        }

        await _store.UpdateDocumentPathAsync(id, target, ct).ConfigureAwait(false);
        LogRetried(job.Id, job.FileName);
        return QueueActionResult.Success();
    }

    public async Task<QueueActionResult> CancelAsync(Guid id, CancellationToken ct)
    {
        var job = await _store.GetAsync(id, ct).ConfigureAwait(false);
        if (job is null)
        {
            return QueueActionResult.Failure("The job does not exist.");
        }

        if (!JobStates.IsActive(job.State))
        {
            return QueueActionResult.Failure("The job has already finished.");
        }

        if (!await _store.CancelAsync(id, CancelReason, ct).ConfigureAwait(false))
        {
            return QueueActionResult.Failure("The job has just finished and cannot be cancelled.");
        }

        var warning = job.State == JobState.Printing ? await CancelOnPrinterAsync(job, ct).ConfigureAwait(false) : null;
        if (job.State != JobState.Converting && !await _mover.MoveDocumentAsync(job, QueueFolders.Cancelled, ct).ConfigureAwait(false))
        {
            LogMoveFailed(job.Id);
        }

        LogCancelled(job.Id, job.FileName);
        return QueueActionResult.Success(warning);
    }

    private async Task<string?> CancelOnPrinterAsync(Job job, CancellationToken ct)
    {
        if (job.BackendJobId is null)
        {
            return null;
        }

        try
        {
            return await _backend.CancelJobAsync(job.BackendJobId, ct).ConfigureAwait(false)
                ? null
                : "The job is cancelled in the queue, but the printer did not confirm it: check the printer queue.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or HttpRequestException or TimeoutException or ArgumentException)
        {
            LogPrinterCancelFailed(ex, job.Id);
            return $"The job is cancelled in the queue, but the printer could not cancel it ({ex.Message}): check the printer queue.";
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Job {JobId} ('{FileName}') put back in the queue by the user.")]
    private partial void LogRetried(Guid jobId, string fileName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Job {JobId} ('{FileName}') cancelled by the user.")]
    private partial void LogCancelled(Guid jobId, string fileName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not move the document of cancelled job {JobId} to its folder.")]
    private partial void LogMoveFailed(Guid jobId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The printer could not cancel job {JobId}.")]
    private partial void LogPrinterCancelFailed(Exception exception, Guid jobId);
}

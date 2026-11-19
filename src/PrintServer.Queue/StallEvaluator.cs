using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrintServer.Core.Printing;
using PrintServer.Core.Queue;

namespace PrintServer.Queue;

public sealed partial class StallEvaluator
{
    private readonly IPrinterBackend _backend;
    private readonly IJobStore _store;
    private readonly JobFileMover _mover;
    private readonly QueueOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<StallEvaluator> _logger;

    public StallEvaluator(
        IPrinterBackend backend,
        IJobStore store,
        IJobFiles files,
        IOptions<QueueOptions> options,
        TimeProvider time,
        ILogger<StallEvaluator> logger)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);

        _backend = backend;
        _store = store;
        _mover = new JobFileMover(files, store);
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    public async Task<StallVerdict> EvaluateAsync(Job job, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (job.State != JobState.Printing)
        {
            return StallVerdict.NotStalled;
        }

        var now = _time.GetUtcNow();
        var reference = new[] { job.StartedAt ?? job.CreatedAt, job.LastBlockedAt ?? DateTimeOffset.MinValue }.Max();
        if (now - reference < _options.StallAfter)
        {
            return StallVerdict.NotStalled;
        }

        if (await PrinterIsBlockedAsync(job, ct).ConfigureAwait(false))
        {
            await _store.MarkBlockedAsync(job.Id, ct).ConfigureAwait(false);
            LogPrinterBlocked(job.Id, job.PrinterName);
            return StallVerdict.NotStalled;
        }

        var reported = await ReportedStatusAsync(job, ct).ConfigureAwait(false);
        if (reported == JobStatus.Completed)
        {
            return await FinishAsync(job, StallVerdict.Completed, QueueFolders.Completed, null, ct).ConfigureAwait(false);
        }

        if (reported == JobStatus.Error)
        {
            return await FinishAsync(job, StallVerdict.Failed, QueueFolders.Failed, "The printer reported an error for the job.", ct).ConfigureAwait(false);
        }

        var reason = $"Stalled: no progress for {_options.StallAfter} while the printer reported no problem.";
        return await FinishAsync(job, StallVerdict.Stalled, QueueFolders.Stalled, reason, ct).ConfigureAwait(false);
    }

    private async Task<StallVerdict> FinishAsync(Job job, StallVerdict verdict, string folder, string? reason, CancellationToken ct)
    {
        var applied = verdict switch
        {
            StallVerdict.Completed => await _store.CompleteAsync(job.Id, ct).ConfigureAwait(false),
            StallVerdict.Failed => await _store.FailAsync(job.Id, reason!, ct).ConfigureAwait(false),
            _ => await _store.StallAsync(job.Id, reason!, ct).ConfigureAwait(false),
        };
        if (!applied)
        {
            return StallVerdict.NotStalled;
        }

        if (!await _mover.MoveDocumentAsync(job, folder, ct).ConfigureAwait(false))
        {
            LogMoveFailed(job.Id, folder);
        }

        LogJobEnded(job.Id, verdict);
        return verdict;
    }

    private async Task<bool> PrinterIsBlockedAsync(Job job, CancellationToken ct)
    {
        try
        {
            var printers = await _backend.ListPrintersAsync(ct).ConfigureAwait(false);
            var printer = printers.FirstOrDefault(candidate => job.PrinterName is null
                ? candidate.IsDefault
                : string.Equals(candidate.Name, job.PrinterName, StringComparison.OrdinalIgnoreCase));
            return printer is null || printer.IsBlocked;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or HttpRequestException or TimeoutException)
        {
            return true;
        }
    }

    private async Task<JobStatus?> ReportedStatusAsync(Job job, CancellationToken ct)
    {
        if (job.BackendJobId is null)
        {
            return null;
        }

        try
        {
            return await _backend.GetJobStatusAsync(job.BackendJobId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or HttpRequestException or TimeoutException)
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Job {JobId} is waiting: printer '{Printer}' reports a problem or cannot be reached.")]
    private partial void LogPrinterBlocked(Guid jobId, string? printer);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job {JobId} ended as {Verdict}.")]
    private partial void LogJobEnded(Guid jobId, StallVerdict verdict);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not move the document of job {JobId} to '{Folder}'.")]
    private partial void LogMoveFailed(Guid jobId, string folder);
}

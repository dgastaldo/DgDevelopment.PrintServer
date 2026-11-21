using System.Net.Http;
using Microsoft.Extensions.Logging;
using PrintServer.Core.Printing;
using PrintServer.Core.Queue;

namespace PrintServer.Queue;

public sealed partial class JobProcessor
{
    private readonly IPrinterBackend _backend;
    private readonly IJobStore _store;
    private readonly IJobFiles _files;
    private readonly JobFileMover _mover;
    private readonly StallEvaluator _stalls;
    private readonly IDocumentConverter _converter;
    private readonly RetryPolicy _retries;
    private readonly ILogger<JobProcessor> _logger;

    public JobProcessor(
        IPrinterBackend backend,
        IJobStore store,
        IJobFiles files,
        StallEvaluator stalls,
        IDocumentConverter converter,
        RetryPolicy retries,
        ILogger<JobProcessor> logger)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(stalls);
        ArgumentNullException.ThrowIfNull(converter);
        ArgumentNullException.ThrowIfNull(retries);
        ArgumentNullException.ThrowIfNull(logger);

        _backend = backend;
        _store = store;
        _files = files;
        _mover = new JobFileMover(files, store);
        _stalls = stalls;
        _converter = converter;
        _retries = retries;
        _logger = logger;
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        for (var next = await _store.ClaimNextConversionAsync(ct).ConfigureAwait(false);
            next is not null;
            next = await _store.ClaimNextConversionAsync(ct).ConfigureAwait(false))
        {
            await ConvertAsync(next, ct).ConfigureAwait(false);
        }

        foreach (var printing in await _store.ListAsync(JobState.Printing, ct).ConfigureAwait(false))
        {
            await CheckPrintingAsync(printing, ct).ConfigureAwait(false);
        }

        for (var next = await _store.ClaimNextAsync(ct).ConfigureAwait(false);
            next is not null;
            next = await _store.ClaimNextAsync(ct).ConfigureAwait(false))
        {
            await SendAsync(next, ct).ConfigureAwait(false);
        }
    }

    private async Task ConvertAsync(Job job, CancellationToken ct)
    {
        if (!_files.Exists(job.DocumentPath))
        {
            await FailAsync(job, "The file of the job is missing.", ct).ConfigureAwait(false);
            return;
        }

        if (!_converter.CanConvert(job.SourceFormat))
        {
            await FailAsync(job, $"Files of type '{job.SourceFormat}' cannot be converted to a printable format.", ct).ConfigureAwait(false);
            return;
        }

        ConversionResult result;
        try
        {
            result = await _converter
                .ConvertToPdfAsync(job.DocumentPath, job.SourceFormat, JobPaths.ConvertedFile(job.DocumentPath), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or TimeoutException)
        {
            result = ConversionResult.Failure($"The conversion failed: {ex.Message}");
        }

        if (!result.Succeeded)
        {
            await FailAsync(job, result.Error ?? "The conversion failed.", ct).ConfigureAwait(false);
            return;
        }

        if (await _store.FinishConversionAsync(job.Id, DocumentFormats.Pdf, ct).ConfigureAwait(false))
        {
            LogConverted(job.Id, job.FileName);
            return;
        }

        await MoveAsync(job, QueueFolders.Cancelled, ct).ConfigureAwait(false);
    }

    private async Task CheckPrintingAsync(Job job, CancellationToken ct)
    {
        var status = await ReportedStatusAsync(job, ct).ConfigureAwait(false);
        switch (status)
        {
            case JobStatus.Completed:
                if (await _store.CompleteAsync(job.Id, ct).ConfigureAwait(false))
                {
                    await MoveAsync(job, QueueFolders.Completed, ct).ConfigureAwait(false);
                    LogCompleted(job.Id, job.FileName);
                }

                break;
            case JobStatus.Error:
                await RetryOrFailAsync(job, "The printer reported an error for the job.", ct).ConfigureAwait(false);
                break;
            default:
                await _stalls.EvaluateAsync(job, ct).ConfigureAwait(false);
                break;
        }
    }

    private async Task SendAsync(Job job, CancellationToken ct)
    {
        if (!_files.Exists(JobPaths.PrintFile(job)))
        {
            await FailAsync(job, "The file of the job is missing.", ct).ConfigureAwait(false);
            return;
        }

        IReadOnlyList<PrinterInfo> printers;
        try
        {
            printers = await _backend.ListPrintersAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsBackendFailure(ex))
        {
            await RetryOrFailAsync(job, $"Could not list the printers: {ex.Message}", ct).ConfigureAwait(false);
            return;
        }

        var printer = FindPrinter(printers, job.PrinterName);
        if (printer is null)
        {
            var reason = job.PrinterName is null
                ? "No default printer is available."
                : $"Printer '{job.PrinterName}' was not found.";
            await FailAsync(job, reason, ct).ConfigureAwait(false);
            return;
        }

        PrintResult result;
        try
        {
            var request = new PrintRequest(
                JobPaths.PrintFile(job),
                printer.Name,
                job.Options.Copies,
                job.FileName,
                job.Options.Orientation,
                job.Options.Duplex);
            result = await _backend.PrintAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsBackendFailure(ex))
        {
            await RetryOrFailAsync(job, $"The backend could not print the job: {ex.Message}", ct).ConfigureAwait(false);
            return;
        }

        if (result.IsPermanent)
        {
            await FailAsync(job, result.Error ?? "The backend rejected the job.", ct).ConfigureAwait(false);
            return;
        }

        if (!result.Accepted)
        {
            await RetryOrFailAsync(job, result.Error ?? "The backend refused the job.", ct).ConfigureAwait(false);
            return;
        }

        if (!await _store.SetBackendJobIdAsync(job.Id, result.BackendJobId!, ct).ConfigureAwait(false))
        {
            await CancelOrphanAsync(job, result.BackendJobId!, ct).ConfigureAwait(false);
            return;
        }

        LogSent(job.Id, job.FileName, printer.Name);
    }

    private async Task CancelOrphanAsync(Job job, string backendJobId, CancellationToken ct)
    {
        try
        {
            await _backend.CancelJobAsync(backendJobId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsBackendFailure(ex))
        {
            LogOrphanNotCancelled(ex, job.Id);
        }
    }

    private static PrinterInfo? FindPrinter(IReadOnlyList<PrinterInfo> printers, string? name) =>
        printers.FirstOrDefault(candidate => name is null
            ? candidate.IsDefault
            : string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));

    private async Task RetryOrFailAsync(Job job, string reason, CancellationToken ct)
    {
        if (_retries.IsExhausted(job.Attempts))
        {
            await FailAsync(job, $"{reason} (gave up after {job.Attempts} attempts)", ct).ConfigureAwait(false);
            return;
        }

        var delay = _retries.DelayAfter(job.Attempts);
        if (await _store.RetryLaterAsync(job.Id, reason, _retries.NextAttemptAfter(job.Attempts), ct).ConfigureAwait(false))
        {
            LogRetry(job.Id, job.FileName, job.Attempts, delay, reason);
        }
    }

    private async Task FailAsync(Job job, string reason, CancellationToken ct)
    {
        if (!await _store.FailAsync(job.Id, reason, ct).ConfigureAwait(false))
        {
            return;
        }

        await MoveAsync(job, QueueFolders.Failed, ct).ConfigureAwait(false);
        LogFailed(job.Id, job.FileName, reason);
    }

    private async Task MoveAsync(Job job, string folder, CancellationToken ct)
    {
        if (!await _mover.MoveDocumentAsync(job, folder, ct).ConfigureAwait(false))
        {
            LogMoveFailed(job.Id, folder);
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
        catch (Exception ex) when (IsBackendFailure(ex))
        {
            LogStatusUnavailable(ex, job.Id);
            return null;
        }
    }

    private static bool IsBackendFailure(Exception ex) =>
        ex is InvalidOperationException or IOException or HttpRequestException or TimeoutException;

    [LoggerMessage(Level = LogLevel.Information, Message = "Job {JobId} ('{FileName}') sent to printer '{Printer}'.")]
    private partial void LogSent(Guid jobId, string fileName, string printer);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job {JobId} was cancelled while it was being sent, and the printer could not drop it.")]
    private partial void LogOrphanNotCancelled(Exception exception, Guid jobId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Job {JobId} ('{FileName}') converted to PDF.")]
    private partial void LogConverted(Guid jobId, string fileName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Job {JobId} ('{FileName}') completed.")]
    private partial void LogCompleted(Guid jobId, string fileName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job {JobId} ('{FileName}') failed attempt {Attempt}; retrying in {Delay}: {Reason}")]
    private partial void LogRetry(Guid jobId, string fileName, int attempt, TimeSpan delay, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Job {JobId} ('{FileName}') failed: {Reason}")]
    private partial void LogFailed(Guid jobId, string fileName, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not move the document of job {JobId} to '{Folder}'.")]
    private partial void LogMoveFailed(Guid jobId, string folder);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not ask the backend about job {JobId}.")]
    private partial void LogStatusUnavailable(Exception exception, Guid jobId);
}

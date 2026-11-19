using Microsoft.Extensions.Logging;
using PrintServer.Core.Printing;
using PrintServer.Core.Queue;
using PrintServer.Core.Watching;

namespace PrintServer.Queue;

public sealed partial class JobIntake
{
    private readonly IJobStore _store;
    private readonly IJobFiles _files;
    private readonly JobFileMover _mover;
    private readonly StallEvaluator _stalls;
    private readonly ILogger<JobIntake> _logger;

    public JobIntake(IJobStore store, IJobFiles files, StallEvaluator stalls, ILogger<JobIntake> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(stalls);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _files = files;
        _mover = new JobFileMover(files, store);
        _stalls = stalls;
        _logger = logger;
    }

    public async Task<IntakeResult> IntakeAsync(DetectedFile file, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (!_files.Exists(file.FilePath))
        {
            LogMissing(file.FilePath);
            return IntakeResult.Of(IntakeOutcome.Skipped);
        }

        string hash;
        try
        {
            hash = await _files.ComputeHashAsync(file.FilePath, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogUnreadable(ex, file.FilePath);
            return IntakeResult.Of(IntakeOutcome.Skipped);
        }

        var origin = Path.GetDirectoryName(Path.GetFullPath(file.FilePath))!;
        var name = Path.GetFileName(file.FilePath);
        var active = await ActiveJobsAsync(origin, name, ct).ConfigureAwait(false);

        var duplicateOf = active.FirstOrDefault(job => IsDuplicate(job, hash, file.CreatedUtc));
        if (duplicateOf is not null)
        {
            return MoveToDuplicates(file, origin, name, duplicateOf);
        }

        return await EnqueueAsync(file, origin, name, hash, active, ct).ConfigureAwait(false);
    }

    private static bool IsDuplicate(Job job, string hash, DateTimeOffset createdUtc)
    {
        if (!string.Equals(job.FileHash, hash, StringComparison.Ordinal))
        {
            return false;
        }

        return job.FileCreatedUtc == createdUtc || job.State == JobState.Printing || createdUtc <= job.FileCreatedUtc;
    }

    private async Task<IReadOnlyList<Job>> ActiveJobsAsync(string origin, string name, CancellationToken ct)
    {
        var active = new List<Job>();
        foreach (var job in await _store.FindActiveAsync(origin, name, ct).ConfigureAwait(false))
        {
            var verdict = await _stalls.EvaluateAsync(job, ct).ConfigureAwait(false);
            if (verdict == StallVerdict.NotStalled)
            {
                active.Add(job);
            }
        }

        return active;
    }

    private IntakeResult MoveToDuplicates(DetectedFile file, string origin, string name, Job duplicateOf)
    {
        LogDuplicate(file.FilePath, duplicateOf.Id, duplicateOf.State);
        try
        {
            _mover.MoveToFolder(file.FilePath, origin, QueueFolders.Duplicates, name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogDuplicateNotMoved(ex, file.FilePath);
        }

        return IntakeResult.Of(IntakeOutcome.Duplicate, duplicateOf.Id);
    }

    private async Task<IntakeResult> EnqueueAsync(
        DetectedFile file,
        string origin,
        string name,
        string hash,
        IReadOnlyList<Job> active,
        CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var documentPath = Path.Combine(origin, QueueFolders.Processing, id.ToString("N"), name);
        var newJob = new NewJob(
            id,
            origin,
            name,
            file.FilePath,
            documentPath,
            file.PrinterName,
            file.CreatedUtc,
            hash,
            file.Length,
            DocumentFormats.ForFile(name),
            PrintOptions.Default);

        if (!await _store.TryEnqueueAsync(newJob, ct).ConfigureAwait(false))
        {
            LogRaced(file.FilePath);
            return IntakeResult.Of(IntakeOutcome.Duplicate);
        }

        try
        {
            _files.CreateDirectory(Path.GetDirectoryName(documentPath)!);
            _files.Move(file.FilePath, documentPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogMoveFailed(ex, file.FilePath);
            await _store.FailAsync(id, $"Could not move the file to the processing folder: {ex.Message}", ct).ConfigureAwait(false);
            return IntakeResult.Of(IntakeOutcome.MoveFailed, id);
        }

        var replaced = await ReplaceAsync(active, hash, ct).ConfigureAwait(false);
        LogEnqueued(file.FilePath, id, file.PrinterName);
        return new IntakeResult(IntakeOutcome.Enqueued, id, replaced);
    }

    private async Task<IReadOnlyList<Guid>> ReplaceAsync(IReadOnlyList<Job> active, string hash, CancellationToken ct)
    {
        var replaced = new List<Guid>();
        foreach (var old in active.Where(job => job.State is JobState.Pending or JobState.Converting))
        {
            var reason = string.Equals(old.FileHash, hash, StringComparison.Ordinal)
                ? "Replaced by a new arrival of the same file."
                : "Replaced by a newer version of the file.";
            if (!await _store.FailAsync(old.Id, reason, ct).ConfigureAwait(false))
            {
                continue;
            }

            replaced.Add(old.Id);
            if (!await _mover.MoveDocumentAsync(old, QueueFolders.Failed, ct).ConfigureAwait(false))
            {
                LogReplacedNotMoved(old.Id);
            }
        }

        return replaced;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping '{Path}': the file no longer exists.")]
    private partial void LogMissing(string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping '{Path}': the file cannot be read.")]
    private partial void LogUnreadable(Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Ignoring '{Path}': it duplicates job {JobId} ({State}).")]
    private partial void LogDuplicate(string path, Guid jobId, JobState state);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not move the duplicate '{Path}' to the duplicates folder.")]
    private partial void LogDuplicateNotMoved(Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Ignoring '{Path}': an identical job was queued at the same moment.")]
    private partial void LogRaced(string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not move '{Path}' to the processing folder.")]
    private partial void LogMoveFailed(Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not move the document of replaced job {JobId} to the errors folder.")]
    private partial void LogReplacedNotMoved(Guid jobId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Queued '{Path}' as job {JobId} for printer '{Printer}'.")]
    private partial void LogEnqueued(string path, Guid jobId, string? printer);
}

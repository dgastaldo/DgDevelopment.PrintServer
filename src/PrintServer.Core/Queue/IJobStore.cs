namespace PrintServer.Core.Queue;

public interface IJobStore
{
    Task<bool> TryEnqueueAsync(NewJob job, CancellationToken ct);

    Task<Job?> GetAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<Job>> ListAsync(JobState state, CancellationToken ct);

    Task<IReadOnlyList<Job>> FindActiveAsync(string originDirectory, string fileName, CancellationToken ct);

    Task<Job?> ClaimNextAsync(CancellationToken ct);

    Task<Job?> ClaimNextConversionAsync(CancellationToken ct);

    Task<bool> FinishConversionAsync(Guid id, string documentFormat, CancellationToken ct);

    Task<bool> SetBackendJobIdAsync(Guid id, string backendJobId, CancellationToken ct);

    Task<bool> MarkBlockedAsync(Guid id, CancellationToken ct);

    Task<bool> CompleteAsync(Guid id, CancellationToken ct);

    Task<bool> FailAsync(Guid id, string reason, CancellationToken ct);

    Task<bool> StallAsync(Guid id, string reason, CancellationToken ct);

    Task<bool> RetryLaterAsync(Guid id, string reason, DateTimeOffset nextAttemptAt, CancellationToken ct);

    Task<bool> RequeueAsync(Guid id, CancellationToken ct);

    Task<bool> UpdateDocumentPathAsync(Guid id, string documentPath, CancellationToken ct);
}

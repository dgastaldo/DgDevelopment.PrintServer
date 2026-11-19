using PrintServer.Core.Printing;

namespace PrintServer.Core.Queue;

public sealed record Job(
    Guid Id,
    string OriginDirectory,
    string FileName,
    string SourcePath,
    string DocumentPath,
    string? PrinterName,
    DateTimeOffset FileCreatedUtc,
    string FileHash,
    long FileSize,
    string SourceFormat,
    string DocumentFormat,
    PrintOptions Options,
    JobState State,
    int Attempts,
    DateTimeOffset? NextAttemptAt,
    string? LastError,
    string? BackendJobId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset? LastBlockedAt);

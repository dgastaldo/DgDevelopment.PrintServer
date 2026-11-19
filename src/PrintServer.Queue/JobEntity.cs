using PrintServer.Core.Queue;

namespace PrintServer.Queue;

public sealed class JobEntity
{
    public Guid Id { get; set; }

    public string OriginDirectory { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string SourcePath { get; set; } = string.Empty;

    public string DocumentPath { get; set; } = string.Empty;

    public string? PrinterName { get; set; }

    public DateTimeOffset FileCreatedAt { get; set; }

    public string FileHash { get; set; } = string.Empty;

    public long FileSize { get; set; }

    public string SourceFormat { get; set; } = string.Empty;

    public string DocumentFormat { get; set; } = string.Empty;

    public string OptionsJson { get; set; } = "{}";

    public JobState State { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset? NextAttemptAt { get; set; }

    public string? LastError { get; set; }

    public string? BackendJobId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public DateTimeOffset? LastBlockedAt { get; set; }
}

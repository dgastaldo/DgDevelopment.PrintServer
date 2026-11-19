using PrintServer.Core.Printing;

namespace PrintServer.Core.Queue;

public sealed record NewJob(
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
    PrintOptions Options);

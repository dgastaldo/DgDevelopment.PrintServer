using System.Text.Json;
using System.Text.Json.Serialization;
using PrintServer.Core.Printing;
using PrintServer.Core.Queue;

namespace PrintServer.Queue;

internal static class JobMapping
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string SerializeOptions(PrintOptions options) => JsonSerializer.Serialize(options, JsonOptions);

    public static PrintOptions DeserializeOptions(string json) =>
        JsonSerializer.Deserialize<PrintOptions>(json, JsonOptions) ?? PrintOptions.Default;

    public static JobEntity ToEntity(NewJob job, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(job);

        return new JobEntity
        {
            Id = job.Id,
            OriginDirectory = job.OriginDirectory,
            FileName = job.FileName,
            SourcePath = job.SourcePath,
            DocumentPath = job.DocumentPath,
            PrinterName = job.PrinterName,
            FileCreatedAt = job.FileCreatedUtc,
            FileHash = job.FileHash,
            FileSize = job.FileSize,
            SourceFormat = job.SourceFormat,
            DocumentFormat = job.SourceFormat,
            OptionsJson = SerializeOptions(job.Options),
            State = JobState.Pending,
            CreatedAt = now,
        };
    }

    public static Job ToJob(JobEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        return new Job(
            entity.Id,
            entity.OriginDirectory,
            entity.FileName,
            entity.SourcePath,
            entity.DocumentPath,
            entity.PrinterName,
            entity.FileCreatedAt,
            entity.FileHash,
            entity.FileSize,
            entity.SourceFormat,
            entity.DocumentFormat,
            DeserializeOptions(entity.OptionsJson),
            entity.State,
            entity.Attempts,
            entity.NextAttemptAt,
            entity.LastError,
            entity.BackendJobId,
            entity.CreatedAt,
            entity.StartedAt,
            entity.FinishedAt,
            entity.LastBlockedAt);
    }
}

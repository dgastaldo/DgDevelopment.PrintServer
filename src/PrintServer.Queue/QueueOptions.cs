namespace PrintServer.Queue;

public sealed class QueueOptions
{
    public const string SectionName = "Queue";

    public string? DatabasePath { get; set; }

    public TimeSpan StallAfter { get; set; } = TimeSpan.FromHours(1);
}

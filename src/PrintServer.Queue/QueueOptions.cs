namespace PrintServer.Queue;

public sealed class QueueOptions
{
    public const string SectionName = "Queue";

    public string? DatabasePath { get; set; }

    public TimeSpan StallAfter { get; set; } = TimeSpan.FromHours(1);

    public int MaxAttempts { get; set; } = 5;

    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
}

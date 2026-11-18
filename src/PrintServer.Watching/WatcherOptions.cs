namespace PrintServer.Watching;

public sealed class WatcherOptions
{
    public const string SectionName = "Watcher";

    public string? Path { get; set; }

    public IReadOnlyList<string>? Extensions { get; set; }

    public IReadOnlyList<string>? IgnoredFolders { get; set; }

    public TimeSpan StabilityWindow { get; set; } = TimeSpan.FromSeconds(10);

    public bool UsePolling { get; set; }

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);

    public bool ProcessExistingFiles { get; set; } = true;

    public int QueueCapacity { get; set; } = 256;
}

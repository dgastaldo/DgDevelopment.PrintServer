namespace PrintServer.Core.Queue;

public static class QueueFolders
{
    public const string Processing = "processing";

    public const string Completed = "processati";

    public const string Failed = "errori";

    public const string Stalled = "stallo";

    public const string Duplicates = "duplicati";

    public const string Cancelled = "annullati";

    public static IReadOnlyList<string> All { get; } = [Processing, Completed, Failed, Stalled, Duplicates, Cancelled];
}

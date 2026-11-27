namespace PrintServer.Core.Queue;

public static class QueueFolders
{
    public const string Processing = "processing";

    public const string Completed = "processed";

    public const string Failed = "errors";

    public const string Stalled = "stalled";

    public const string Duplicates = "duplicates";

    public const string Cancelled = "cancelled";

    public static IReadOnlyList<string> All { get; } = [Processing, Completed, Failed, Stalled, Duplicates, Cancelled];
}

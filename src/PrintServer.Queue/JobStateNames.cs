using PrintServer.Core.Queue;

namespace PrintServer.Queue;

internal static class JobStateNames
{
    public const string Pending = "pending";
    public const string Converting = "converting";
    public const string Printing = "printing";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Stalled = "stalled";
    public const string Cancelled = "cancelled";

    public static string ToDatabase(JobState state) => state switch
    {
        JobState.Pending => Pending,
        JobState.Converting => Converting,
        JobState.Printing => Printing,
        JobState.Completed => Completed,
        JobState.Failed => Failed,
        JobState.Stalled => Stalled,
        JobState.Cancelled => Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown job state."),
    };

    public static JobState FromDatabase(string value) => value switch
    {
        Pending => JobState.Pending,
        Converting => JobState.Converting,
        Printing => JobState.Printing,
        Completed => JobState.Completed,
        Failed => JobState.Failed,
        Stalled => JobState.Stalled,
        Cancelled => JobState.Cancelled,
        _ => throw new InvalidOperationException($"Unknown job state '{value}' in the database."),
    };
}

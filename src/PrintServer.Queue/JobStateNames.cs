using PrintServer.Core.Queue;

namespace PrintServer.Queue;

internal static class JobStateNames
{
    public const string Pending = "in_attesa";
    public const string Converting = "in_conversione";
    public const string Printing = "in_stampa";
    public const string Completed = "completato";
    public const string Failed = "fallito";
    public const string Stalled = "in_stallo";
    public const string Cancelled = "annullato";

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

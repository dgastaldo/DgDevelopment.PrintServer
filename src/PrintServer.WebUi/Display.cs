using PrintServer.Core.Queue;

namespace PrintServer.WebUi;

/// <summary>Language-independent presentation rules for the page; the words live in <see cref="UiText"/>.</summary>
public static class Display
{
    public static IReadOnlyList<JobState> AllStates { get; } =
        [JobState.Pending, JobState.Converting, JobState.Printing, JobState.Completed, JobState.Failed, JobState.Stalled, JobState.Cancelled];

    public static string StateLabel(JobState state) => state switch
    {
        JobState.Pending => "Pending",
        JobState.Converting => "Converting",
        JobState.Printing => "Printing",
        JobState.Completed => "Completed",
        JobState.Failed => "Failed",
        JobState.Stalled => "Stalled",
        JobState.Cancelled => "Cancelled",
        _ => state.ToString(),
    };

    public static string StateClass(JobState state) => state switch
    {
        JobState.Completed => "ok",
        JobState.Failed or JobState.Stalled => "bad",
        JobState.Cancelled => "muted",
        _ => "info",
    };

    public static bool CanRetry(JobState state) => state is JobState.Failed or JobState.Stalled or JobState.Cancelled;

    public static bool CanCancel(JobState state) => JobStates.IsActive(state);
}

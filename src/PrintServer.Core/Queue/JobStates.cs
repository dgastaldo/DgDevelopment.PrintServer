namespace PrintServer.Core.Queue;

public static class JobStates
{
    public static bool IsActive(JobState state) => state is JobState.Pending or JobState.Converting or JobState.Printing;
}

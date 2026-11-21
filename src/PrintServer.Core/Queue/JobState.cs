namespace PrintServer.Core.Queue;

public enum JobState
{
    Pending,
    Converting,
    Printing,
    Completed,
    Failed,
    Stalled,
    Cancelled,
}

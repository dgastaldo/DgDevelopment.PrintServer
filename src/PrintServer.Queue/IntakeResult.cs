namespace PrintServer.Queue;

public enum IntakeOutcome
{
    Enqueued,
    Duplicate,
    Skipped,
    MoveFailed,
}

public sealed record IntakeResult(IntakeOutcome Outcome, Guid? JobId, IReadOnlyList<Guid> ReplacedJobIds)
{
    public static IntakeResult Of(IntakeOutcome outcome, Guid? jobId = null) => new(outcome, jobId, []);
}

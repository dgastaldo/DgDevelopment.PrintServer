namespace PrintServer.Core.Watching;

public sealed record SubmissionResult
{
    private SubmissionResult(bool succeeded, string? error)
    {
        Succeeded = succeeded;
        Error = error;
    }

    public bool Succeeded { get; }

    public string? Error { get; }

    public static SubmissionResult Success() => new(true, null);

    public static SubmissionResult Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new SubmissionResult(false, error);
    }
}

namespace PrintServer.Core.Printing;

public sealed record PrintResult
{
    private PrintResult(bool accepted, string? backendJobId, string? error, bool isPermanent)
    {
        Accepted = accepted;
        BackendJobId = backendJobId;
        Error = error;
        IsPermanent = isPermanent;
    }

    public bool Accepted { get; }

    public string? BackendJobId { get; }

    public string? Error { get; }

    public bool IsPermanent { get; }

    public static PrintResult Success(string backendJobId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backendJobId);
        return new PrintResult(true, backendJobId, null, false);
    }

    public static PrintResult Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new PrintResult(false, null, error, false);
    }

    public static PrintResult Rejected(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new PrintResult(false, null, error, true);
    }
}

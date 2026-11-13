namespace PrintServer.Core.Printing;

public sealed record PrintResult
{
    private PrintResult(bool accepted, string? backendJobId, string? error)
    {
        Accepted = accepted;
        BackendJobId = backendJobId;
        Error = error;
    }

    public bool Accepted { get; }

    public string? BackendJobId { get; }

    public string? Error { get; }

    public static PrintResult Success(string backendJobId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backendJobId);
        return new PrintResult(true, backendJobId, null);
    }

    public static PrintResult Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new PrintResult(false, null, error);
    }
}

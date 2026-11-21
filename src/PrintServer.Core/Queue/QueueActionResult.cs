namespace PrintServer.Core.Queue;

public sealed record QueueActionResult
{
    private QueueActionResult(bool succeeded, string? message)
    {
        Succeeded = succeeded;
        Message = message;
    }

    public bool Succeeded { get; }

    public string? Message { get; }

    public static QueueActionResult Success(string? warning = null) => new(true, warning);

    public static QueueActionResult Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new QueueActionResult(false, error);
    }
}

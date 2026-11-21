namespace PrintServer.Core.Printing;

public sealed record ConversionResult
{
    private ConversionResult(bool succeeded, string? error)
    {
        Succeeded = succeeded;
        Error = error;
    }

    public bool Succeeded { get; }

    public string? Error { get; }

    public static ConversionResult Success() => new(true, null);

    public static ConversionResult Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new ConversionResult(false, error);
    }
}

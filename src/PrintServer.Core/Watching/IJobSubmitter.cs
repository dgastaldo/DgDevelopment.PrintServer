using PrintServer.Core.Printing;

namespace PrintServer.Core.Watching;

public interface IJobSubmitter
{
    IReadOnlyList<string> AcceptedExtensions { get; }

    Task<SubmissionResult> SubmitAsync(string fileName, Stream content, string? printerName, PrintOptions options, CancellationToken ct);
}

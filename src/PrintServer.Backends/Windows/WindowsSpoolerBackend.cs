using PrintServer.Backends.Common;
using PrintServer.Core.Printing;

namespace PrintServer.Backends.Windows;

public sealed class WindowsSpoolerBackend(ISpoolerApi spooler) : IPrinterBackend
{
    public Task<IReadOnlyList<PrinterInfo>> ListPrintersAsync(CancellationToken ct) =>
        Task.Run<IReadOnlyList<PrinterInfo>>(
            () =>
            {
                var defaultPrinter = spooler.GetDefaultPrinterName();
                return spooler.EnumeratePrinters()
                    .Select(p => new PrinterInfo(
                        p.Name,
                        string.Equals(p.Name, defaultPrinter, StringComparison.OrdinalIgnoreCase),
                        SpoolerStatusMapper.MapPrinter(p.Status)))
                    .ToList();
            },
            ct);

    public Task<PrintResult> PrintAsync(PrintRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.PrinterName))
        {
            return Task.FromResult(PrintResult.Failure("Printer name is required."));
        }

        if (request.Copies != 1)
        {
            return Task.FromResult(PrintResult.Failure("The Windows spooler backend sends raw data and supports exactly one copy per job."));
        }

        var fullPath = Path.GetFullPath(request.FilePath);
        if (!File.Exists(fullPath))
        {
            return Task.FromResult(PrintResult.Failure($"File '{request.FilePath}' does not exist."));
        }

        var documentName = string.IsNullOrWhiteSpace(request.JobName) ? Path.GetFileName(fullPath) : request.JobName;
        return Task.Run(() => Submit(request.PrinterName, documentName, fullPath, ct), ct);
    }

    public Task<JobStatus> GetJobStatusAsync(string backendJobId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backendJobId);

        if (!BackendJobId.TryParse(backendJobId, out var jobId, out var printerName))
        {
            throw new ArgumentException($"'{backendJobId}' is not a Windows spooler job id.", nameof(backendJobId));
        }

        return Task.Run(
            () =>
            {
                var status = spooler.GetJobStatus(printerName, jobId);
                return status is null ? JobStatus.Completed : SpoolerStatusMapper.MapJob(status.Value);
            },
            ct);
    }

    private PrintResult Submit(string printerName, string documentName, string fullPath, CancellationToken ct)
    {
        try
        {
            using var stream = File.OpenRead(fullPath);
            var jobId = spooler.SubmitRawDocument(printerName, documentName, stream, ct);
            return PrintResult.Success(BackendJobId.Format(jobId, printerName));
        }
        catch (InvalidOperationException ex)
        {
            return PrintResult.Failure(ex.Message);
        }
        catch (IOException ex)
        {
            return PrintResult.Failure($"Cannot read '{fullPath}': {ex.Message}");
        }
    }
}

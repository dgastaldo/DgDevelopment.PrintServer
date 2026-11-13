namespace PrintServer.Core.Printing;

public interface IPrinterBackend
{
    Task<IReadOnlyList<PrinterInfo>> ListPrintersAsync(CancellationToken ct);

    Task<PrintResult> PrintAsync(PrintRequest request, CancellationToken ct);

    Task<JobStatus> GetJobStatusAsync(string backendJobId, CancellationToken ct);
}

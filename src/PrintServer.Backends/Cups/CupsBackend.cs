using System.Globalization;
using Microsoft.Extensions.Options;
using PrintServer.Backends.Common;
using PrintServer.Core.Printing;

namespace PrintServer.Backends.Cups;

public sealed class CupsBackend(IProcessRunner runner, IOptions<CupsOptions> options) : IPrinterBackend
{
    private readonly CupsOptions _options = options.Value;

    public async Task<IReadOnlyList<PrinterInfo>> ListPrintersAsync(CancellationToken ct)
    {
        var result = await runner.RunAsync(_options.LpstatPath, ["-l", "-p", "-d"], ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            if (CupsOutputParser.IsNoDestinations(result.StandardError))
            {
                return [];
            }

            throw new InvalidOperationException($"lpstat failed: {result.StandardError.Trim()}");
        }

        return CupsOutputParser.ParsePrinters(result.StandardOutput);
    }

    public async Task<PrintResult> PrintAsync(PrintRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.PrinterName))
        {
            return PrintResult.Failure("Printer name is required.");
        }

        if (request.Copies < 1)
        {
            return PrintResult.Failure("Copies must be at least 1.");
        }

        var fullPath = Path.GetFullPath(request.FilePath);
        if (!File.Exists(fullPath))
        {
            return PrintResult.Failure($"File '{request.FilePath}' does not exist.");
        }

        var jobName = string.IsNullOrWhiteSpace(request.JobName) ? Path.GetFileName(fullPath) : request.JobName;
        List<string> arguments =
        [
            "-d", request.PrinterName,
            "-n", request.Copies.ToString(CultureInfo.InvariantCulture),
            "-t", jobName,
        ];
        AddLayoutOptions(arguments, request);
        arguments.Add(fullPath);

        ProcessResult result;
        try
        {
            result = await runner.RunAsync(_options.LpPath, arguments, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            return PrintResult.Failure(ex.Message);
        }

        if (result.ExitCode != 0)
        {
            return PrintResult.Failure($"lp failed: {result.StandardError.Trim()}");
        }

        var jobId = CupsOutputParser.ParseRequestId(result.StandardOutput);
        return jobId is null
            ? PrintResult.Failure($"Unexpected lp output: {result.StandardOutput.Trim()}")
            : PrintResult.Success(jobId);
    }

    private static void AddLayoutOptions(List<string> arguments, PrintRequest request)
    {
        var orientation = request.Orientation switch
        {
            PageOrientation.Portrait => "orientation-requested=3",
            PageOrientation.Landscape => "orientation-requested=4",
            _ => null,
        };
        var sides = request.Duplex switch
        {
            DuplexMode.OneSided => "sides=one-sided",
            DuplexMode.TwoSidedLongEdge => "sides=two-sided-long-edge",
            DuplexMode.TwoSidedShortEdge => "sides=two-sided-short-edge",
            _ => null,
        };

        foreach (var option in new[] { orientation, sides }.OfType<string>())
        {
            arguments.Add("-o");
            arguments.Add(option);
        }
    }

    public async Task<JobStatus> GetJobStatusAsync(string backendJobId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backendJobId);

        var pending = await runner.RunAsync(_options.LpstatPath, ["-W", "not-completed", "-o"], ct).ConfigureAwait(false);
        if (pending.ExitCode != 0)
        {
            throw new InvalidOperationException($"lpstat failed: {pending.StandardError.Trim()}");
        }

        if (!CupsOutputParser.ParsePendingJobIds(pending.StandardOutput).Contains(backendJobId))
        {
            return JobStatus.Completed;
        }

        var printer = CupsOutputParser.PrinterOfJob(backendJobId);
        var printerState = await runner.RunAsync(_options.LpstatPath, ["-p", printer], ct).ConfigureAwait(false);
        if (printerState.ExitCode != 0)
        {
            throw new InvalidOperationException($"lpstat failed: {printerState.StandardError.Trim()}");
        }

        return CupsOutputParser.IsPrintingJob(printerState.StandardOutput, backendJobId)
            ? JobStatus.Printing
            : JobStatus.Queued;
    }
}

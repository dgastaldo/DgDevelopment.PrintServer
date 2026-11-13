using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Options;
using PrintServer.Core.Printing;

namespace PrintServer.Backends.Fake;

public sealed class FakePrinterBackend(IOptions<FakePrinterOptions> options, TimeProvider timeProvider) : IPrinterBackend
{
    private const string DefaultPrinterName = "Fake Printer";

    private readonly FakePrinterOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, FakeJob> _jobs = new();
    private long _nextJobId;

    public Task<IReadOnlyList<PrinterInfo>> ListPrintersAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<PrinterInfo> printers = PrinterNames()
            .Select((name, index) => new PrinterInfo(name, index == 0, PrinterAvailability.Ready))
            .ToList();
        return Task.FromResult(printers);
    }

    public Task<PrintResult> PrintAsync(PrintRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        if (!PrinterNames().Contains(request.PrinterName))
        {
            return Task.FromResult(PrintResult.Failure($"Unknown printer '{request.PrinterName}'."));
        }

        if (request.Copies < 1)
        {
            return Task.FromResult(PrintResult.Failure("Copies must be at least 1."));
        }

        if (!File.Exists(request.FilePath))
        {
            return Task.FromResult(PrintResult.Failure($"File '{request.FilePath}' does not exist."));
        }

        var jobId = Interlocked.Increment(ref _nextJobId).ToString(CultureInfo.InvariantCulture);
        var failure = _options.FailureRule?.Invoke(request);
        _jobs[jobId] = new FakeJob(timeProvider.GetUtcNow(), failure);
        return Task.FromResult(PrintResult.Success(jobId));
    }

    public Task<JobStatus> GetJobStatusAsync(string backendJobId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!_jobs.TryGetValue(backendJobId, out var job))
        {
            throw new KeyNotFoundException($"Unknown job '{backendJobId}'.");
        }

        var elapsed = timeProvider.GetUtcNow() - job.CreatedAt;
        if (elapsed < _options.QueueDelay)
        {
            return Task.FromResult(JobStatus.Queued);
        }

        if (elapsed < _options.QueueDelay + _options.PrintDuration)
        {
            return Task.FromResult(JobStatus.Printing);
        }

        return Task.FromResult(job.Failure is null ? JobStatus.Completed : JobStatus.Error);
    }

    private IList<string> PrinterNames() => _options.Printers.Count > 0 ? _options.Printers : [DefaultPrinterName];

    private sealed record FakeJob(DateTimeOffset CreatedAt, string? Failure);
}

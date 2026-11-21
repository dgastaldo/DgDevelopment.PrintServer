using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PrintServer.Core.Printing;
using PrintServer.Core.Queue;
using PrintServer.Core.Watching;
using PrintServer.Queue;

namespace PrintServer.UnitTests;

internal sealed class ScriptedPrinterBackend : IPrinterBackend
{
    public List<PrinterInfo> Printers { get; } = [new PrinterInfo("Office", IsDefault: true, PrinterAvailability.Ready)];

    public Dictionary<string, JobStatus> Statuses { get; } = [];

    public Exception? ThrowOnList { get; set; }

    public Exception? ThrowOnStatus { get; set; }

    public List<PrintRequest> Requests { get; } = [];

    public Exception? ThrowOnPrint { get; set; }

    public string? RefuseWith { get; set; }

    public string? RejectWith { get; set; }

    public Task<IReadOnlyList<PrinterInfo>> ListPrintersAsync(CancellationToken ct)
    {
        if (ThrowOnList is not null)
        {
            throw ThrowOnList;
        }

        return Task.FromResult<IReadOnlyList<PrinterInfo>>(Printers);
    }

    public Task<PrintResult> PrintAsync(PrintRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        if (ThrowOnPrint is not null)
        {
            throw ThrowOnPrint;
        }

        if (RejectWith is not null)
        {
            return Task.FromResult(PrintResult.Rejected(RejectWith));
        }

        return Task.FromResult(RefuseWith is null
            ? PrintResult.Success(Requests.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))
            : PrintResult.Failure(RefuseWith));
    }

    public Task<JobStatus> GetJobStatusAsync(string backendJobId, CancellationToken ct)
    {
        if (ThrowOnStatus is not null)
        {
            throw ThrowOnStatus;
        }

        return Task.FromResult(Statuses.GetValueOrDefault(backendJobId, JobStatus.Printing));
    }
}

internal sealed class FlakyJobFiles : IJobFiles
{
    private readonly PhysicalJobFiles _inner = new();

    public bool FailHash { get; set; }

    public bool FailMove { get; set; }

    public bool Exists(string path) => _inner.Exists(path);

    public Task<string> ComputeHashAsync(string path, CancellationToken ct) =>
        FailHash ? throw new IOException("locked") : _inner.ComputeHashAsync(path, ct);

    public void CreateDirectory(string path) => _inner.CreateDirectory(path);

    public void Move(string source, string destination)
    {
        if (FailMove)
        {
            throw new IOException("disk full");
        }

        _inner.Move(source, destination);
    }

    public void DeleteDirectoryIfEmpty(string path) => _inner.DeleteDirectoryIfEmpty(path);
}

internal sealed class RacingJobStore(IJobStore inner) : IJobStore
{
    public Task<bool> TryEnqueueAsync(NewJob job, CancellationToken ct) => Task.FromResult(false);

    public Task<Job?> GetAsync(Guid id, CancellationToken ct) => inner.GetAsync(id, ct);

    public Task<IReadOnlyList<Job>> ListAsync(JobState state, CancellationToken ct) => inner.ListAsync(state, ct);

    public Task<IReadOnlyList<Job>> FindActiveAsync(string originDirectory, string fileName, CancellationToken ct) =>
        inner.FindActiveAsync(originDirectory, fileName, ct);

    public Task<Job?> ClaimNextAsync(CancellationToken ct) => inner.ClaimNextAsync(ct);

    public Task<bool> SetBackendJobIdAsync(Guid id, string backendJobId, CancellationToken ct) =>
        inner.SetBackendJobIdAsync(id, backendJobId, ct);

    public Task<bool> MarkBlockedAsync(Guid id, CancellationToken ct) => inner.MarkBlockedAsync(id, ct);

    public Task<bool> CompleteAsync(Guid id, CancellationToken ct) => inner.CompleteAsync(id, ct);

    public Task<bool> FailAsync(Guid id, string reason, CancellationToken ct) => inner.FailAsync(id, reason, ct);

    public Task<bool> StallAsync(Guid id, string reason, CancellationToken ct) => inner.StallAsync(id, reason, ct);

    public Task<bool> RetryLaterAsync(Guid id, string reason, DateTimeOffset nextAttemptAt, CancellationToken ct) =>
        inner.RetryLaterAsync(id, reason, nextAttemptAt, ct);

    public Task<bool> RequeueAsync(Guid id, CancellationToken ct) => inner.RequeueAsync(id, ct);

    public Task<bool> UpdateDocumentPathAsync(Guid id, string documentPath, CancellationToken ct) =>
        inner.UpdateDocumentPathAsync(id, documentPath, ct);
}

internal sealed class QueueHarness : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"printserver-flow-{Guid.NewGuid():N}");

    public QueueHarness()
    {
        Directory.CreateDirectory(Inbox);
        Queue = new SqliteQueueFixture();
        Options = new QueueOptions { StallAfter = TimeSpan.FromHours(1) };
        Stalls = new StallEvaluator(Backend, Queue.Store, Files, Microsoft.Extensions.Options.Options.Create(Options), Queue.Time, NullLogger<StallEvaluator>.Instance);
        Intake = new JobIntake(Queue.Store, Files, Stalls, NullLogger<JobIntake>.Instance);
        Recovery = new JobRecovery(Backend, Queue.Store, Files, NullLogger<JobRecovery>.Instance);
        Processor = new JobProcessor(Backend, Queue.Store, Files, Stalls, Microsoft.Extensions.Options.Options.Create(Options), Queue.Time, NullLogger<JobProcessor>.Instance);
    }

    public SqliteQueueFixture Queue { get; }

    public ScriptedPrinterBackend Backend { get; } = new();

    public FlakyJobFiles Files { get; } = new();

    public QueueOptions Options { get; }

    public StallEvaluator Stalls { get; }

    public JobIntake Intake { get; }

    public JobRecovery Recovery { get; }

    public JobProcessor Processor { get; }

    public string Inbox => Path.Combine(_root, "inbox");

    public DateTimeOffset Now => Queue.Time.GetUtcNow();

    public string Folder(string? printer, string? subfolder = null)
    {
        var parts = new List<string> { Inbox };
        if (printer is not null)
        {
            parts.Add(printer);
        }

        if (subfolder is not null)
        {
            parts.Add(subfolder);
        }

        return Path.Combine([.. parts]);
    }

    public DetectedFile Drop(string? printer, string name, string content, DateTimeOffset? created = null)
    {
        var directory = Folder(printer);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, content);
        return new DetectedFile(path, printer, new FileInfo(path).Length, created ?? SqliteQueueFixture.Start.AddHours(-2));
    }

    public async Task<IntakeResult> IntakeAsync(DetectedFile file) => await Intake.IntakeAsync(file, CancellationToken.None);

    public async Task<Job> GetAsync(Guid id) => (await Queue.Store.GetAsync(id, CancellationToken.None))!;

    public async Task<Job> StartPrintingAsync(Guid id)
    {
        var claimed = await Queue.Store.ClaimNextAsync(CancellationToken.None);
        Assert.Equal(id, claimed!.Id);
        return claimed;
    }

    public void Dispose()
    {
        Queue.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}

internal sealed class FailingFirstLookupJobStore(IJobStore inner) : IJobStore
{
    private int _lookups;

    public Task<bool> TryEnqueueAsync(NewJob job, CancellationToken ct) => inner.TryEnqueueAsync(job, ct);

    public Task<Job?> GetAsync(Guid id, CancellationToken ct) => inner.GetAsync(id, ct);

    public Task<IReadOnlyList<Job>> ListAsync(JobState state, CancellationToken ct) => inner.ListAsync(state, ct);

    public Task<IReadOnlyList<Job>> FindActiveAsync(string originDirectory, string fileName, CancellationToken ct) =>
        Interlocked.Increment(ref _lookups) == 1
            ? throw new InvalidOperationException("database unavailable")
            : inner.FindActiveAsync(originDirectory, fileName, ct);

    public Task<Job?> ClaimNextAsync(CancellationToken ct) => inner.ClaimNextAsync(ct);

    public Task<bool> SetBackendJobIdAsync(Guid id, string backendJobId, CancellationToken ct) =>
        inner.SetBackendJobIdAsync(id, backendJobId, ct);

    public Task<bool> MarkBlockedAsync(Guid id, CancellationToken ct) => inner.MarkBlockedAsync(id, ct);

    public Task<bool> CompleteAsync(Guid id, CancellationToken ct) => inner.CompleteAsync(id, ct);

    public Task<bool> FailAsync(Guid id, string reason, CancellationToken ct) => inner.FailAsync(id, reason, ct);

    public Task<bool> StallAsync(Guid id, string reason, CancellationToken ct) => inner.StallAsync(id, reason, ct);

    public Task<bool> RetryLaterAsync(Guid id, string reason, DateTimeOffset nextAttemptAt, CancellationToken ct) =>
        inner.RetryLaterAsync(id, reason, nextAttemptAt, ct);

    public Task<bool> RequeueAsync(Guid id, CancellationToken ct) => inner.RequeueAsync(id, ct);

    public Task<bool> UpdateDocumentPathAsync(Guid id, string documentPath, CancellationToken ct) =>
        inner.UpdateDocumentPathAsync(id, documentPath, ct);
}

using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using PrintServer.Core.Queue;
using PrintServer.Core.Watching;
using PrintServer.Queue;

namespace PrintServer.UnitTests;

public sealed class QueueFilesAndServiceTests : IDisposable
{
    private readonly QueueHarness _h = new();
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), $"printserver-files-{Guid.NewGuid():N}");

    public QueueFilesAndServiceTests() => Directory.CreateDirectory(_scratch);

    public void Dispose()
    {
        _h.Dispose();
        Directory.Delete(_scratch, recursive: true);
    }

    [Fact]
    public async Task TheHashIsTheSha256OfTheContentInUpperCaseHex()
    {
        var path = Path.Combine(_scratch, "a.pdf");
        await File.WriteAllTextAsync(path, "hello");

        var hash = await new PhysicalJobFiles().ComputeHashAsync(path, CancellationToken.None);

        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("hello"))), hash);
    }

    [Fact]
    public async Task HashingAMissingFileThrows()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() => new PhysicalJobFiles().ComputeHashAsync(Path.Combine(_scratch, "missing.pdf"), CancellationToken.None));
    }

    [Fact]
    public void FilesAreCreatedMovedAndEmptyFoldersRemoved()
    {
        var files = new PhysicalJobFiles();
        var source = Path.Combine(_scratch, "a.pdf");
        File.WriteAllText(source, "x");
        var folder = Path.Combine(_scratch, "sub", "deep");
        files.CreateDirectory(folder);
        var destination = Path.Combine(folder, "a.pdf");

        files.Move(source, destination);

        Assert.False(files.Exists(source));
        Assert.True(files.Exists(destination));
        files.DeleteDirectoryIfEmpty(folder);
        Assert.True(Directory.Exists(folder));
        File.Delete(destination);
        files.DeleteDirectoryIfEmpty(folder);
        Assert.False(Directory.Exists(folder));
        files.DeleteDirectoryIfEmpty(folder);
    }

    [Fact]
    public async Task MovingADocumentAvoidsOverwritingWhatIsAlreadyInTheDestinationFolder()
    {
        var first = await _h.IntakeAsync(_h.Drop("Office", "a.pdf", "one"));
        var second = await _h.IntakeAsync(_h.Drop("Office", "a.pdf", "two"));
        Assert.Equal(JobState.Failed, (await _h.GetAsync(first.JobId!.Value)).State);
        var third = await _h.IntakeAsync(_h.Drop("Office", "a.pdf", "three"));

        Assert.Equal(JobState.Failed, (await _h.GetAsync(second.JobId!.Value)).State);
        var errors = Directory.GetFiles(_h.Folder("Office", "errors")).Select(path => Path.GetFileName(path)!).Order().ToArray();
        Assert.Equal(["a (1).pdf", "a.pdf"], errors);
        Assert.Equal(JobState.Pending, (await _h.GetAsync(third.JobId!.Value)).State);
    }

    [Fact]
    public async Task TheIntakeServiceQueuesEveryFileItReadsFromTheChannel()
    {
        var channel = Channel.CreateUnbounded<DetectedFile>();
        using var service = new JobIntakeService(channel, _h.Intake, NullLogger<JobIntakeService>.Instance);
        await service.StartAsync(CancellationToken.None);

        await channel.Writer.WriteAsync(_h.Drop("Office", "a.pdf", "one"));
        await channel.Writer.WriteAsync(_h.Drop("Labels", "b.pdf", "two"));

        var jobs = await WaitForJobsAsync(2);
        Assert.Equal(["a.pdf", "b.pdf"], jobs.Select(job => job.FileName).Order());
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task AFailureOnOneFileDoesNotStopTheService()
    {
        var channel = Channel.CreateUnbounded<DetectedFile>();
        var intake = new JobIntake(new FailingFirstLookupJobStore(_h.Queue.Store), _h.Files, _h.Stalls, NullLogger<JobIntake>.Instance);
        using var service = new JobIntakeService(channel, intake, NullLogger<JobIntakeService>.Instance);
        await service.StartAsync(CancellationToken.None);

        await channel.Writer.WriteAsync(_h.Drop("Office", "bad.pdf", "one"));
        await channel.Writer.WriteAsync(_h.Drop("Office", "good.pdf", "two"));

        var jobs = await WaitForJobsAsync(1);
        Assert.Equal("good.pdf", Assert.Single(jobs).FileName);
        Assert.True(File.Exists(Path.Combine(_h.Folder("Office"), "bad.pdf")));
        await service.StopAsync(CancellationToken.None);
    }

    private async Task<IReadOnlyList<Job>> WaitForJobsAsync(int expected)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            var jobs = await _h.Queue.Store.ListAsync(JobState.Pending, cts.Token);
            if (jobs.Count >= expected)
            {
                return jobs;
            }

            await Task.Delay(50, cts.Token);
        }
    }
}

using Microsoft.Data.Sqlite;
using PrintServer.Core.Printing;
using PrintServer.Core.Queue;

namespace PrintServer.UnitTests;

public sealed class SqliteJobStoreTests : IDisposable
{
    private readonly SqliteQueueFixture _queue = new();

    public void Dispose() => _queue.Dispose();

    private static CancellationToken Ct => CancellationToken.None;

    [Fact]
    public async Task AnEnqueuedJobStartsPendingWithAllItsFields()
    {
        var options = new PrintOptions(PageOrientation.Landscape, DuplexMode.TwoSidedLongEdge, 2);
        var newJob = SqliteQueueFixture.MakeJob(options: options);

        var job = await _queue.EnqueueAsync(newJob);

        Assert.Equal(newJob.Id, job.Id);
        Assert.Equal(JobState.Pending, job.State);
        Assert.Equal(newJob.OriginDirectory, job.OriginDirectory);
        Assert.Equal(newJob.FileName, job.FileName);
        Assert.Equal(newJob.SourcePath, job.SourcePath);
        Assert.Equal(newJob.DocumentPath, job.DocumentPath);
        Assert.Equal("Office", job.PrinterName);
        Assert.Equal(newJob.FileCreatedUtc, job.FileCreatedUtc);
        Assert.Equal("hash-1", job.FileHash);
        Assert.Equal(1234, job.FileSize);
        Assert.Equal(DocumentFormats.Pdf, job.SourceFormat);
        Assert.Equal(DocumentFormats.Pdf, job.DocumentFormat);
        Assert.Equal(options, job.Options);
        Assert.Equal(0, job.Attempts);
        Assert.Equal(SqliteQueueFixture.Start, job.CreatedAt);
        Assert.Null(job.StartedAt);
        Assert.Null(job.FinishedAt);
        Assert.Null(job.LastError);
        Assert.Null(job.BackendJobId);
    }

    [Fact]
    public async Task AJobWithoutAPrinterKeepsANullPrinter()
    {
        var job = await _queue.EnqueueAsync(SqliteQueueFixture.MakeJob(printer: null));

        Assert.Null(job.PrinterName);
    }

    [Fact]
    public async Task AnUnknownJobIsNotFound()
    {
        Assert.Null(await _queue.Store.GetAsync(Guid.NewGuid(), Ct));
    }

    [Fact]
    public async Task TheSameFileCannotBeActiveTwice()
    {
        var first = SqliteQueueFixture.MakeJob();
        var second = SqliteQueueFixture.MakeJob();

        Assert.True(await _queue.Store.TryEnqueueAsync(first, Ct));
        Assert.False(await _queue.Store.TryEnqueueAsync(second, Ct));
    }

    [Fact]
    public async Task TheSameFileCanBeQueuedAgainOnceTheFirstJobIsFinished()
    {
        var first = await _queue.EnqueueAsync(SqliteQueueFixture.MakeJob());
        await _queue.Store.FailAsync(first.Id, "boom", Ct);

        Assert.True(await _queue.Store.TryEnqueueAsync(SqliteQueueFixture.MakeJob(), Ct));
    }

    [Fact]
    public async Task FilesDifferingInAnyKeyFieldAreDistinct()
    {
        await _queue.EnqueueAsync(SqliteQueueFixture.MakeJob());

        Assert.True(await _queue.Store.TryEnqueueAsync(SqliteQueueFixture.MakeJob(hash: "hash-2"), Ct));
        Assert.True(await _queue.Store.TryEnqueueAsync(SqliteQueueFixture.MakeJob(created: SqliteQueueFixture.Start), Ct));
        Assert.True(await _queue.Store.TryEnqueueAsync(SqliteQueueFixture.MakeJob(name: "b.pdf"), Ct));
        Assert.True(await _queue.Store.TryEnqueueAsync(SqliteQueueFixture.MakeJob(origin: "/inbox/Other"), Ct));
    }

    [Fact]
    public async Task FindActiveReturnsOnlyActiveJobsOfThatFile()
    {
        var active = await _queue.EnqueueAsync(SqliteQueueFixture.MakeJob(hash: "h1"));
        var finished = await _queue.EnqueueAsync(SqliteQueueFixture.MakeJob(hash: "h2"));
        await _queue.Store.FailAsync(finished.Id, "boom", Ct);
        await _queue.EnqueueAsync(SqliteQueueFixture.MakeJob(name: "other.pdf"));
        await _queue.EnqueueAsync(SqliteQueueFixture.MakeJob(origin: "/inbox/Other"));

        var found = await _queue.Store.FindActiveAsync("/inbox/Office", "a.pdf", Ct);

        Assert.Equal(active.Id, Assert.Single(found).Id);
    }

    [Fact]
    public async Task ListReturnsTheJobsInAStateOldestFirst()
    {
        var first = await _queue.EnqueueAsync(SqliteQueueFixture.MakeJob(name: "1.pdf"));
        _queue.Time.Advance(TimeSpan.FromMinutes(1));
        var second = await _queue.EnqueueAsync(SqliteQueueFixture.MakeJob(name: "2.pdf"));
        var failed = await _queue.EnqueueAsync(SqliteQueueFixture.MakeJob(name: "3.pdf"));
        await _queue.Store.FailAsync(failed.Id, "boom", Ct);

        var pending = await _queue.Store.ListAsync(JobState.Pending, Ct);

        Assert.Equal([first.Id, second.Id], pending.Select(job => job.Id));
        Assert.Single(await _queue.Store.ListAsync(JobState.Failed, Ct));
        Assert.Empty(await _queue.Store.ListAsync(JobState.Completed, Ct));
    }

    [Fact]
    public async Task ClaimingTakesTheOldestPendingJobAndStartsPrinting()
    {
        var first = await _queue.EnqueueAsync(SqliteQueueFixture.MakeJob(name: "1.pdf", printer: "A"));
        _queue.Time.Advance(TimeSpan.FromMinutes(1));
        await _queue.EnqueueAsync(SqliteQueueFixture.MakeJob(name: "2.pdf", printer: "B"));

        var claimed = await _queue.Store.ClaimNextAsync(Ct);

        Assert.NotNull(claimed);
        Assert.Equal(first.Id, claimed.Id);
        Assert.Equal(JobState.Printing, claimed.State);
        Assert.Equal(1, claimed.Attempts);
        Assert.Equal(_queue.Time.GetUtcNow(), claimed.StartedAt);
    }

    [Fact]
    public async Task ClaimingWithNothingPendingReturnsNull()
    {
        Assert.Null(await _queue.Store.ClaimNextAsync(Ct));
    }

    [Fact]
    public async Task AJobWaitingForARetryIsNotClaimableBeforeItsTime()
    {
        await _queue.EnqueueAsync(SqliteQueueFixture.MakeJob());
        var claimed = (await _queue.Store.ClaimNextAsync(Ct))!;
        var retryAt = _queue.Time.GetUtcNow().AddMinutes(5);
        Assert.True(await _queue.Store.RetryLaterAsync(claimed.Id, "printer busy", retryAt, Ct));

        Assert.Null(await _queue.Store.ClaimNextAsync(Ct));

        _queue.Time.Advance(TimeSpan.FromMinutes(5));
        var again = await _queue.Store.ClaimNextAsync(Ct);
        Assert.NotNull(again);
        Assert.Equal(2, again.Attempts);
        Assert.Equal("printer busy", again.LastError);
    }

    [Fact]
    public async Task OnlyOneJobPerPrinterIsPrintingAtATime()
    {
        await _queue.EnqueueAsync(SqliteQueueFixture.MakeJob(name: "1.pdf", printer: "A"));
        _queue.Time.Advance(TimeSpan.FromSeconds(1));
        await _queue.EnqueueAsync(SqliteQueueFixture.MakeJob(name: "2.pdf", printer: "A"));
        _queue.Time.Advance(TimeSpan.FromSeconds(1));
        var other = await _queue.EnqueueAsync(SqliteQueueFixture.MakeJob(name: "3.pdf", printer: "B"));

        var first = (await _queue.Store.ClaimNextAsync(Ct))!;
        var second = (await _queue.Store.ClaimNextAsync(Ct))!;
        var third = await _queue.Store.ClaimNextAsync(Ct);

        Assert.Equal("1.pdf", first.FileName);
        Assert.Equal(other.Id, second.Id);
        Assert.Null(third);

        await _queue.Store.CompleteAsync(first.Id, Ct);
        Assert.Equal("2.pdf", (await _queue.Store.ClaimNextAsync(Ct))!.FileName);
    }

    [Fact]
    public async Task JobsWithoutAPrinterShareTheDefaultPrinterSlot()
    {
        await _queue.EnqueueAsync(SqliteQueueFixture.MakeJob(name: "1.pdf", printer: null));
        _queue.Time.Advance(TimeSpan.FromSeconds(1));
        await _queue.EnqueueAsync(SqliteQueueFixture.MakeJob(name: "2.pdf", printer: null));

        Assert.NotNull(await _queue.Store.ClaimNextAsync(Ct));
        Assert.Null(await _queue.Store.ClaimNextAsync(Ct));
    }

    [Fact]
    public async Task CompletingFinishesAPrintingJobOnly()
    {
        var pending = await _queue.EnqueueAsync(SqliteQueueFixture.MakeJob());
        Assert.False(await _queue.Store.CompleteAsync(pending.Id, Ct));

        var claimed = (await _queue.Store.ClaimNextAsync(Ct))!;
        _queue.Time.Advance(TimeSpan.FromMinutes(2));
        Assert.True(await _queue.Store.CompleteAsync(claimed.Id, Ct));

        var done = (await _queue.Store.GetAsync(claimed.Id, Ct))!;
        Assert.Equal(JobState.Completed, done.State);
        Assert.Equal(_queue.Time.GetUtcNow(), done.FinishedAt);
        Assert.False(await _queue.Store.CompleteAsync(claimed.Id, Ct));
    }

    [Theory]
    [InlineData(JobState.Pending)]
    [InlineData(JobState.Converting)]
    [InlineData(JobState.Printing)]
    public async Task AnActiveJobCanFailWithAReason(JobState from)
    {
        var job = await _queue.EnqueueAsync(SqliteQueueFixture.MakeJob());
        _queue.SetState(job.Id, from);

        Assert.True(await _queue.Store.FailAsync(job.Id, "replaced by a newer version", Ct));

        var failed = (await _queue.Store.GetAsync(job.Id, Ct))!;
        Assert.Equal(JobState.Failed, failed.State);
        Assert.Equal("replaced by a newer version", failed.LastError);
        Assert.NotNull(failed.FinishedAt);
    }

    [Theory]
    [InlineData(JobState.Completed)]
    [InlineData(JobState.Failed)]
    [InlineData(JobState.Stalled)]
    public async Task AFinishedJobCannotFail(JobState from)
    {
        var job = await _queue.EnqueueAsync(SqliteQueueFixture.MakeJob());
        _queue.SetState(job.Id, from);

        Assert.False(await _queue.Store.FailAsync(job.Id, "late", Ct));
        Assert.Equal(from, (await _queue.Store.GetAsync(job.Id, Ct))!.State);
    }

    [Fact]
    public async Task OnlyAPrintingJobCanStall()
    {
        var job = await _queue.EnqueueAsync(SqliteQueueFixture.MakeJob());
        Assert.False(await _queue.Store.StallAsync(job.Id, "no progress", Ct));

        var claimed = (await _queue.Store.ClaimNextAsync(Ct))!;
        Assert.True(await _queue.Store.StallAsync(claimed.Id, "no progress for 1 hour", Ct));

        var stalled = (await _queue.Store.GetAsync(job.Id, Ct))!;
        Assert.Equal(JobState.Stalled, stalled.State);
        Assert.Equal("no progress for 1 hour", stalled.LastError);
        Assert.NotNull(stalled.FinishedAt);
    }

    [Fact]
    public async Task ARetryClearsTheBackendTrackingOfTheAttempt()
    {
        await _queue.EnqueueAsync(SqliteQueueFixture.MakeJob());
        var claimed = (await _queue.Store.ClaimNextAsync(Ct))!;
        Assert.True(await _queue.Store.SetBackendJobIdAsync(claimed.Id, "42", Ct));
        Assert.True(await _queue.Store.MarkBlockedAsync(claimed.Id, Ct));

        Assert.True(await _queue.Store.RetryLaterAsync(claimed.Id, "again", _queue.Time.GetUtcNow(), Ct));

        var retried = (await _queue.Store.GetAsync(claimed.Id, Ct))!;
        Assert.Equal(JobState.Pending, retried.State);
        Assert.Null(retried.BackendJobId);
        Assert.Null(retried.StartedAt);
        Assert.Null(retried.LastBlockedAt);
    }

    [Fact]
    public async Task BackendTrackingAppliesToPrintingJobsOnly()
    {
        var job = await _queue.EnqueueAsync(SqliteQueueFixture.MakeJob());

        Assert.False(await _queue.Store.SetBackendJobIdAsync(job.Id, "42", Ct));
        Assert.False(await _queue.Store.MarkBlockedAsync(job.Id, Ct));
        Assert.False(await _queue.Store.RetryLaterAsync(job.Id, "again", _queue.Time.GetUtcNow(), Ct));

        var claimed = (await _queue.Store.ClaimNextAsync(Ct))!;
        _queue.Time.Advance(TimeSpan.FromMinutes(3));
        Assert.True(await _queue.Store.SetBackendJobIdAsync(claimed.Id, "42", Ct));
        Assert.True(await _queue.Store.MarkBlockedAsync(claimed.Id, Ct));

        var tracked = (await _queue.Store.GetAsync(claimed.Id, Ct))!;
        Assert.Equal("42", tracked.BackendJobId);
        Assert.Equal(_queue.Time.GetUtcNow(), tracked.LastBlockedAt);
    }

    [Theory]
    [InlineData(JobState.Printing)]
    [InlineData(JobState.Converting)]
    public async Task AJobInFlightCanBeRequeued(JobState from)
    {
        var job = await _queue.EnqueueAsync(SqliteQueueFixture.MakeJob());
        _queue.SetState(job.Id, from);

        Assert.True(await _queue.Store.RequeueAsync(job.Id, Ct));

        Assert.Equal(JobState.Pending, (await _queue.Store.GetAsync(job.Id, Ct))!.State);
        Assert.False(await _queue.Store.RequeueAsync(job.Id, Ct));
    }

    [Fact]
    public async Task TheDocumentPathCanBeUpdatedInAnyState()
    {
        var job = await _queue.EnqueueAsync(SqliteQueueFixture.MakeJob());

        Assert.True(await _queue.Store.UpdateDocumentPathAsync(job.Id, "/inbox/Office/processati/a.pdf", Ct));

        Assert.Equal("/inbox/Office/processati/a.pdf", (await _queue.Store.GetAsync(job.Id, Ct))!.DocumentPath);
        Assert.False(await _queue.Store.UpdateDocumentPathAsync(Guid.NewGuid(), "/x", Ct));
    }

    [Fact]
    public async Task StatesAreStoredAsReadableNames()
    {
        var job = await _queue.EnqueueAsync(SqliteQueueFixture.MakeJob());
        var states = new List<string> { await ReadStateAsync(job.Id) };
        await _queue.Store.ClaimNextAsync(Ct);
        states.Add(await ReadStateAsync(job.Id));
        await _queue.Store.CompleteAsync(job.Id, Ct);
        states.Add(await ReadStateAsync(job.Id));

        Assert.Equal(["in_attesa", "in_stampa", "completato"], states);
    }

    [Fact]
    public async Task PrintOptionsAreStoredAsReadableJson()
    {
        var job = await _queue.EnqueueAsync(SqliteQueueFixture.MakeJob(options: new PrintOptions(PageOrientation.Landscape, DuplexMode.TwoSidedShortEdge, 3)));

        using var connection = new SqliteConnection($"Data Source={_queue.DatabasePath}");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT options FROM jobs WHERE id = $id";
        command.Parameters.AddWithValue("$id", job.Id.ToString().ToUpperInvariant());
        var json = (string?)await command.ExecuteScalarAsync();

        Assert.Equal("""{"orientation":"landscape","duplex":"twoSidedShortEdge","copies":3}""", json);
    }

    [Fact]
    public async Task ArgumentsAreValidated()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _queue.Store.TryEnqueueAsync(null!, Ct));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _queue.Store.FailAsync(Guid.NewGuid(), " ", Ct));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _queue.Store.StallAsync(Guid.NewGuid(), "", Ct));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _queue.Store.RetryLaterAsync(Guid.NewGuid(), " ", _queue.Time.GetUtcNow(), Ct));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _queue.Store.SetBackendJobIdAsync(Guid.NewGuid(), "", Ct));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _queue.Store.UpdateDocumentPathAsync(Guid.NewGuid(), " ", Ct));
    }

    private async Task<string> ReadStateAsync(Guid id)
    {
        using var connection = new SqliteConnection($"Data Source={_queue.DatabasePath}");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT state FROM jobs WHERE id = $id";
        command.Parameters.AddWithValue("$id", id.ToString().ToUpperInvariant());
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
